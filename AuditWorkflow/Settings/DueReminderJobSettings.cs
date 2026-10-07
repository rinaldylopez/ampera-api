namespace AuditWorkflow.Settings;

public class DueReminderJobSettings
{
    /// <summary>Si es false, el hosted service no ejecuta el procesamiento.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hora del servidor (0-23) preferida para la primera corrida diaria.</summary>
    public int RunAtHourUtc { get; set; } = 12;

    /// <summary>Intervalo entre comprobaciones del hosted service (horas).</summary>
    public int CheckIntervalHours { get; set; } = 1;

    /// <summary>Clave opcional para invocar POST /api/jobs/due-reminders/run desde un scheduler externo.</summary>
    public string JobApiKey { get; set; } = "";

    /// <summary>
    /// Activa el digest diario de solicitudes vencidas:
    /// un solo correo por destinatario con todas las vencidas (máx. 1 vez al día).
    /// </summary>
    public bool OverdueWeeklyEnabled { get; set; } = true;

    /// <summary>Notificar a personal de firma en el digest de vencidos.</summary>
    public bool OverdueNotifyFirmStaff { get; set; } = true;

    /// <summary>Notificar a usuarios portal cliente en el digest de vencidos.</summary>
    public bool OverdueNotifyClientPortal { get; set; } = true;
}
