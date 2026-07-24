namespace AuditWorkflow.Settings
{
    public class AppSettings
    {
        public string Secret { get; set; }

        /// <summary>Duración del JWT de acceso en minutos (refresh automático antes de expirar). Por defecto 60.</summary>
        public int JwtAccessTokenMinutes { get; set; } = 60;
    }
}
