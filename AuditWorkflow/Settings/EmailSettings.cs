namespace AuditWorkflow.Settings;

public class EmailSettings
{
    /// <summary>Si es false, no se intenta SMTP (solo se registra en log).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Google Workspace / Gmail (envío autenticado como buzón): <c>smtp.gmail.com</c> puerto <c>587</c> (STARTTLS).
    /// Relay SMTP de administrador (sin credenciales de buzón, según política del admin): a veces <c>smtp-relay.gmail.com</c>.
    /// </summary>
    public string SmtpHost { get; set; }

    /// <summary>Por defecto 587 (Workspace/Gmail recomendado). Opcional 465 (SSL).</summary>
    public int SmtpPort { get; set; } = 587;

    public string User { get; set; }
    public string Password { get; set; }

    public string FromAddress { get; set; }
    public string FromName { get; set; } = "AMPERA";

    /// <summary>URL base del front (portal), sin barra final. Ej: https://app.ejemplo.com</summary>
    public string PortalBaseUrl { get; set; }
}
