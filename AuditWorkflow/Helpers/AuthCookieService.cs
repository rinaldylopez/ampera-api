using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Helpers;

public class AuthCookieService(IOptions<AuthCookieSettings> options, IHostEnvironment environment)
{
    private readonly AuthCookieSettings _settings = options.Value ?? new AuthCookieSettings();
    private readonly IHostEnvironment _environment = environment;

    public string AccessCookieName => _settings.AccessCookieName;
    public string RefreshCookieName => _settings.RefreshCookieName;

    public bool UseSecureCookies => _environment.IsProduction() || _settings.Secure;

    public void SetAuthCookies(HttpResponse response, string accessToken, string refreshToken, int accessMinutes)
    {
        var sameSite = ParseSameSite(_settings.SameSite);
        var secure = UseSecureCookies;
        var accessMaxAge = Math.Max(60, accessMinutes * 60);

        response.Cookies.Append(
            _settings.AccessCookieName,
            accessToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(accessMaxAge)
            });

        response.Cookies.Append(
            _settings.RefreshCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = sameSite,
                Path = string.IsNullOrWhiteSpace(_settings.RefreshCookiePath) ? "/" : _settings.RefreshCookiePath,
                MaxAge = TimeSpan.FromDays(1)
            });
    }

    public void ClearAuthCookies(HttpResponse response)
    {
        var sameSite = ParseSameSite(_settings.SameSite);
        var secure = UseSecureCookies;

        response.Cookies.Delete(_settings.AccessCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Path = "/"
        });

        response.Cookies.Delete(_settings.RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Path = string.IsNullOrWhiteSpace(_settings.RefreshCookiePath) ? "/" : _settings.RefreshCookiePath
        });
    }

    public string? ReadRefreshToken(HttpRequest request, string? bodyRefreshToken)
    {
        if (!string.IsNullOrWhiteSpace(bodyRefreshToken))
            return bodyRefreshToken.Trim();

        return request.Cookies.TryGetValue(_settings.RefreshCookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie)
            ? cookie
            : null;
    }

    public void StripTokensFromResponse(Models.Auth.UserResponse response)
    {
        response.UserToken = "";
        response.RefreshToken = "";
    }

    private static SameSiteMode ParseSameSite(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "none" => SameSiteMode.None,
            "strict" => SameSiteMode.Strict,
            _ => SameSiteMode.Lax
        };
    }
}
