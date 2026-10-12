namespace AuditWorkflow.Services;

/// <summary>Resultado del digest diario de solicitudes vencidas.</summary>
public sealed class OverdueWeeklyProcessResult
{
    public DateOnly ProcessedForDate { get; init; }
    public int OverdueRequestsFound { get; init; }
    public int DigestEmailsSent { get; init; }
    public int RecipientsSkippedAlreadySentToday { get; init; }
    public int RecipientsSkippedNoEmail { get; init; }
}

/// <summary>
/// Digest diario: un correo por destinatario con todas las solicitudes vencidas relevantes.
/// No envía un correo por cada solicitud.
/// </summary>
public interface IRequestOverdueWeeklyReminderService
{
    /// <summary>
    /// Agrupa vencidas por destinatario y envía como máximo un correo/digest por usuario y día.
    /// </summary>
    Task<OverdueWeeklyProcessResult> ProcessOverdueWeeklyRemindersAsync(
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default);
}
