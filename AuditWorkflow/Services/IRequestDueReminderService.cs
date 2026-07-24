namespace AuditWorkflow.Services;

public sealed class DueReminderProcessResult
{
    public DateOnly ProcessedForDate { get; init; }
    public int RulesEvaluated { get; init; }
    public int NotificationsSent { get; init; }
    public int SkippedOutsideWindow { get; init; }
    public int SkippedAlreadyDispatched { get; init; }
    public int SkippedMissingDueDate { get; init; }
}

public interface IRequestDueReminderService
{
    /// <summary>
    /// Evalúa recordatorios activos y envía notificaciones para la fecha indicada (UTC).
    /// Un recordatorio notifica una vez por día calendario mientras hoy esté entre
    /// (DueDate − DaysBeforeDue) y DueDate inclusive.
    /// </summary>
    Task<DueReminderProcessResult> ProcessDueRemindersAsync(
        DateOnly? asOfDateUtc = null,
        CancellationToken cancellationToken = default);
}
