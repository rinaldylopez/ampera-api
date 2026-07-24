using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuditWorkflow.Services;

public class TokenService(IOptions<AppSettings> appSettings) : ITokenService
{
    private readonly AppSettings _appSettings = appSettings.Value ?? new AppSettings();

    private byte[] GetSigningKey()
    {
        var secret = Environment.GetEnvironmentVariable("Secret") ?? _appSettings.Secret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "JWT signing secret is not configured. Set environment variable Secret or AppSettings:Secret.");

        return Encoding.ASCII.GetBytes(secret);
    }

    public async Task<object> GetTokenAsync(User user, AppDbContext context, int activeFirmId, int? activeCustomerId = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var keySecret = GetSigningKey();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.SecurityRole ?? "Standard"),
            new Claim("FirmId", activeFirmId.ToString()),
            new Claim("ProfileId", user.ProfileId.ToString()),
            new Claim("must_change_password", user.MustChangePassword ? "true" : "false")
        };
        if (user.UserType == TenantQueries.UserTypeClient && activeCustomerId is int ac)
            claims.Add(new Claim("CustomerId", ac.ToString()));

        var accessMinutes = _appSettings.JwtAccessTokenMinutes;
        if (accessMinutes <= 0)
            accessMinutes = 60;
        accessMinutes = Math.Clamp(accessMinutes, 5, 12 * 60);

        var accessExpiresUtc = DateTime.UtcNow.AddMinutes(accessMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = accessExpiresUtc,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(keySecret),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(token);

        var oldTokens = await context.UserRefreshTokens
            .Where(t => t.UserId == user.UserId && !t.IsRevoked)
            .ToListAsync();

        foreach (var oldtoken in oldTokens)
        {
            oldtoken.IsRevoked = true;
            oldtoken.IsUsed = true;
        }

        var refreshToken = new UserRefreshToken
        {
            UserId = user.UserId,
            ActiveFirmId = activeFirmId,
            ActiveCustomerId = user.UserType == TenantQueries.UserTypeClient ? activeCustomerId : null,
            Token = Guid.NewGuid().ToString().Replace("-", ""),
            ExpiresAt = DateTime.Now.AddDays(1),
            CreatedAt = DateTime.Now,
            IsRevoked = false,
            IsUsed = false
        };

        context.UserRefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        return new
        {
            accessToken = jwtString,
            refreshToken = refreshToken.Token,
            expires = accessMinutes,
            expiration = accessExpiresUtc
        };
    }
}
