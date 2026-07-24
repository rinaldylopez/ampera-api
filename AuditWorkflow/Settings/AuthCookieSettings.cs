namespace AuditWorkflow.Settings;

/// <summary>Cookies HttpOnly para JWT y refresh (SPA en subdominio distinto al API).</summary>
public class AuthCookieSettings
{
    public const string SectionName = "AuthCookies";

    public string AccessCookieName { get; set; } = "ampera_access";

    public string RefreshCookieName { get; set; } = "ampera_refresh";

    /// <summary>Ruta del refresh cookie (limita envío a endpoints de auth).</summary>
    public string RefreshCookiePath { get; set; } = "/api/UserAuth";

    /// <summary>Lax para www + api bajo el mismo dominio registrable.</summary>
    public string SameSite { get; set; } = "Lax";

    /// <summary>Forzar Secure; en producción siempre true.</summary>
    public bool Secure { get; set; } = true;
}
