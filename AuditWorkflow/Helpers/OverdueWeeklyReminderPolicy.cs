using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Helpers;

/// <summary>
/// Reglas puras de elegibilidad para el digest de solicitudes vencidas (SRP).
/// La frecuencia “una vez al día” la impone el servicio con bitácora por usuario+día.
/// </summary>
public static class OverdueWeeklyReminderPolicy
{
    /// <summary>Status borrador: no entra en el digest de vencidos.</summary>
    public const int DraftStatus = 0;

    /// <summary>
    /// True si la solicitud está abierta, no es borrador y su DueDate (día calendario) es anterior a <paramref name="today"/>.
    /// </summary>
    public static bool IsOverdueOpenRequest(Request request, DateOnly today)
    {
        if (request.ClosedAt != null)
            return false;
        if (request.Status == DraftStatus)
            return false;
        if (request.DueDate is not DateTime due)
            return false;

        var dueDay = DateOnly.FromDateTime(due.Date);
        return dueDay < today;
    }

    /// <summary>Días de atraso (0 si no está vencida).</summary>
    public static int DaysOverdue(Request request, DateOnly today)
    {
        if (request.DueDate is not DateTime due)
            return 0;
        var dueDay = DateOnly.FromDateTime(due.Date);
        var delta = today.DayNumber - dueDay.DayNumber;
        return delta > 0 ? delta : 0;
    }
}
