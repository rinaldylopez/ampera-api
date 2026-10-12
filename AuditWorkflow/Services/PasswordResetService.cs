using System.Security.Cryptography;
using System.Text;
using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Auth;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Services;

public class PasswordResetService(
    IEmailService emailService,
    IOptions<AppSettings> appSettings,
    IOptions<EmailSettings> emailSettings,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public const string EmailNotConfiguredMessage = "PasswordReset:EmailNotConfigured";
    public const string CooldownMessage = "PasswordReset:Cooldown";

    public const int TokenExpiryMinutes = 60;
    public const int MinResendSeconds = 120;
    public const int MaxFailedAttempts = 8;

    private readonly IEmailService _emailService = emailService;
    private readonly AppSettings _app = appSettings.Value ?? new AppSettings();
    private readonly EmailSettings _mail = emailSettings.Value ?? new EmailSettings();
    private readonly ILogger<PasswordResetService> _logger = logger;

    private string Pepper() =>
        Environment.GetEnvironmentVariable("Secret") ?? _app.Secret ?? "";

    private static string HashToken(string rawToken, string pepper)
    {
        var raw = $"{rawToken}:{pepper}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public async Task SendResetLinkAsync(
        User targetUser,
        int? requestedByUserId,
        string firmDisplayName,
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_mail.Enabled || string.IsNullOrWhiteSpace(_mail.SmtpHost))
        {
            _logger.LogWarning("Password reset: SMTP no configurado.");
            throw new InvalidOperationException(EmailNotConfiguredMessage);
        }

        var pepper = Pepper();
        if (string.IsNullOrWhiteSpace(pepper))
        {
            _logger.LogError("Password reset: falta Secret de aplicación.");
            throw new InvalidOperationException(EmailNotConfiguredMessage);
        }

        var now = ServerClock.Now;
        var recent = await context.PasswordResetTokens.AsNoTracking()
            .Where(t => t.UserId == targetUser.UserId && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (recent != null && (now - recent.CreatedAt).TotalSeconds < MinResendSeconds)
            throw new InvalidOperationException(CooldownMessage);

        var pending = await context.PasswordResetTokens
            .Where(t => t.UserId == targetUser.UserId && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        context.PasswordResetTokens.RemoveRange(pending);

        var rawToken = GenerateRawToken();
        var hash = HashToken(rawToken, pepper);

        context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = targetUser.UserId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(TokenExpiryMinutes),
            RequestedByUserId = requestedByUserId,
            FailedAttempts = 0
        });

        // Obliga a cambiar si entrara con la clave vieja antes de usar el enlace.
        targetUser.MustChangePassword = true;
        await context.SaveChangesAsync(cancellationToken);

        var baseUrl = (_mail.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            _logger.LogWarning("PortalBaseUrl no configurado; el enlace de reset puede ser inválido.");

        var resetUrl = string.IsNullOrEmpty(baseUrl)
            ? $"#?token={Uri.EscapeDataString(rawToken)}"
            : $"{baseUrl}/auth/reset-password?token={Uri.EscapeDataString(rawToken)}";

        var html = PasswordResetEmail.BuildHtml(
            targetUser.FullName ?? "",
            resetUrl,
            TokenExpiryMinutes,
            firmDisplayName);
        const string subject = "AMPERA — Enlace para definir su contraseña";

        try
        {
            await _emailService.SendHtmlAsync(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetUser.Email.Trim() },
                subject,
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar enlace de reset a {Email}", targetUser.Email);
            throw;
        }
    }

    public async Task<bool> IsTokenValidAsync(
        string rawToken,
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var row = await FindActiveTokenAsync(rawToken, context, cancellationToken);
        return row != null;
    }

    public async Task<bool> CompleteResetAsync(
        string rawToken,
        string newPassword,
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var pepper = Pepper();
        if (string.IsNullOrWhiteSpace(pepper))
            return false;

        var token = (rawToken ?? "").Trim();
        if (token.Length < 20)
            return false;

        var newPwd = (newPassword ?? "").Trim();
        if (newPwd.Length < 6)
            return false;

        var hash = HashToken(token, pepper);
        var row = await context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (row == null)
            return false;

        var now = ServerClock.Now;
        if (row.UsedAt != null || row.ExpiresAt < now)
            return false;

        if (row.FailedAttempts >= MaxFailedAttempts)
            return false;

        var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == row.UserId, cancellationToken);
        if (user == null || !user.IsActive)
            return false;

        user.PasswordHash = EncryptPassword.HashPassword(newPwd, pepper);
        user.MustChangePassword = false;
        row.UsedAt = now;

        var other = await context.PasswordResetTokens
            .Where(t => t.UserId == user.UserId && t.PasswordResetTokenId != row.PasswordResetTokenId && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        context.PasswordResetTokens.RemoveRange(other);

        var refresh = await context.UserRefreshTokens
            .Where(t => t.UserId == user.UserId && !t.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var rt in refresh)
        {
            rt.IsRevoked = true;
            rt.IsUsed = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<PasswordResetToken?> FindActiveTokenAsync(
        string rawToken,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var pepper = Pepper();
        if (string.IsNullOrWhiteSpace(pepper))
            return null;

        var token = (rawToken ?? "").Trim();
        if (token.Length < 20)
            return null;

        var hash = HashToken(token, pepper);
        var row = await context.PasswordResetTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (row == null)
            return null;

        var now = ServerClock.Now;
        if (row.UsedAt != null || row.ExpiresAt < now)
            return null;
        if (row.FailedAttempts >= MaxFailedAttempts)
            return null;

        return row;
    }
}
