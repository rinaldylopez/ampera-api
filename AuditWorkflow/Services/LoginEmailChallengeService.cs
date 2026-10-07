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

public class LoginEmailChallengeService(
    IUserService userService,
    IUserFirmAccessService firmAccess,
    IUserCustomerAccessService customerAccess,
    IEmailService emailService,
    IOptions<AppSettings> appSettings,
    IOptions<EmailSettings> emailSettings,
    ILogger<LoginEmailChallengeService> logger) : ILoginEmailChallengeService
{
    public const string EmailNotConfiguredMessage = "LoginEmailChallenge:EmailNotConfigured";

    private readonly IUserFirmAccessService _firmAccess = firmAccess;
    private readonly IUserCustomerAccessService _customerAccess = customerAccess;
    private readonly AppSettings _app = appSettings.Value ?? new AppSettings();
    private readonly EmailSettings _mail = emailSettings.Value ?? new EmailSettings();

    private const int CodeExpiryMinutes = 10;
    private const int MaxFailedAttempts = 5;

    private static string HashCode(Guid challengeToken, string sixDigits, string secretPepper)
    {
        var raw = $"{challengeToken:N}:{sixDigits}:{secretPepper}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public async Task<LoginRequestCodeResponse?> RequestCodeAsync(AuthRequest authRequest, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var user = await userService.FindAuthenticatedUserAsync(authRequest, context);
        if (user == null)
            return null;

        if (!_mail.Enabled || string.IsNullOrWhiteSpace(_mail.SmtpHost))
        {
            logger.LogWarning("OTP de login: SMTP deshabilitado o sin host; no se puede enviar el código.");
            throw new InvalidOperationException(EmailNotConfiguredMessage);
        }

        var secret = Environment.GetEnvironmentVariable("Secret") ?? _app.Secret ?? "";
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("OTP de login: falta secreto de aplicación (Secret).");
            throw new InvalidOperationException(EmailNotConfiguredMessage);
        }

        var now = ServerClock.Now;
        var pending = await context.LoginEmailChallenges
            .Where(c => c.UserId == user.UserId && c.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        context.LoginEmailChallenges.RemoveRange(pending);

        var challengeToken = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var hash = HashCode(challengeToken, code, secret);

        context.LoginEmailChallenges.Add(new LoginEmailChallenge
        {
            ChallengeToken = challengeToken,
            UserId = user.UserId,
            CodeHash = hash,
            ExpiresAtUtc = now.AddMinutes(CodeExpiryMinutes),
            CreatedAtUtc = now,
            FailedAttempts = 0
        });

        await context.SaveChangesAsync(cancellationToken);

        var firmName = user.Firm?.Name?.Trim() ?? "AMPERA";
        var baseUrl = (_mail.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        var loginUrl = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/auth/login";

        var subject = "AMPERA — Código de acceso";
        var html = LoginOtpEmail.BuildHtml(user.FullName ?? "", code, CodeExpiryMinutes, loginUrl, firmName);

        await emailService.SendHtmlAsync(new[] { user.Email }, subject, html, cancellationToken);

        return new LoginRequestCodeResponse
        {
            ChallengeToken = challengeToken.ToString("D"),
            ExpiresInSeconds = CodeExpiryMinutes * 60
        };
    }

    public async Task<UserResponse?> VerifyCodeAsync(LoginVerifyCodeRequest request, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim() ?? "";
        if (code.Length != 6 || !code.All(char.IsDigit))
            return null;

        if (!Guid.TryParse(request.ChallengeToken, out var challengeToken))
            return null;

        var secret = Environment.GetEnvironmentVariable("Secret") ?? _app.Secret ?? "";
        if (string.IsNullOrWhiteSpace(secret))
            return null;

        var challenge = await context.LoginEmailChallenges
            .FirstOrDefaultAsync(c => c.ChallengeToken == challengeToken, cancellationToken);

        if (challenge == null)
            return null;

        if (challenge.ExpiresAtUtc < ServerClock.Now)
            return null;

        if (challenge.FailedAttempts >= MaxFailedAttempts)
            return null;

        var expectedHash = HashCode(challengeToken, code, secret);
        byte[] expectedBytes;
        byte[] actualBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedHash);
            actualBytes = Convert.FromHexString(challenge.CodeHash);
        }
        catch (FormatException)
        {
            return null;
        }

        if (expectedBytes.Length != actualBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            challenge.FailedAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var usuario = await context.Users
            .Include(u => u.Firm)
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .FirstOrDefaultAsync(u => u.UserId == challenge.UserId && u.IsActive, cancellationToken);

        if (usuario == null)
            return null;

        var activeFirmId = usuario.FirmId;
        if (request.LoginFirmId is int chosenFirmId)
        {
            if (!await _firmAccess.CanAccessFirmAsync(usuario.UserId, chosenFirmId, context, cancellationToken))
                return null;
            activeFirmId = chosenFirmId;
        }

        int? preferredCustomerId = null;
        if (usuario.UserType == TenantQueries.UserTypeClient)
        {
            if (request.LoginCustomerId is int chosenCustomerId)
            {
                if (!await _customerAccess.CanAccessCustomerAsync(usuario.UserId, chosenCustomerId, context, cancellationToken))
                    return null;
                preferredCustomerId = chosenCustomerId;
            }
        }

        context.LoginEmailChallenges.Remove(challenge);
        await context.SaveChangesAsync(cancellationToken);

        return await userService.CreateLoginResponseAsync(usuario, context, activeFirmId, preferredCustomerId);
    }
}
