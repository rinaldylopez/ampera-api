using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Recordatorio manual por solicitud: fecha propia de vencimiento (<see cref="ReminderDueDate"/>)
/// y avisos N días antes de esa fecha (<see cref="DaysBeforeDue"/>).
/// </summary>
public class RequestReminder
{
    [Key]
    public int RequestReminderId { get; set; }

    public int RequestId { get; set; }
    [ForeignKey(nameof(RequestId))]
    public virtual Request Request { get; set; }

    /// <summary>Fecha de vencimiento del recordatorio (independiente de <see cref="Request.DueDate"/>).</summary>
    public DateTime ReminderDueDate { get; set; }

    /// <summary>Días antes de <see cref="ReminderDueDate"/> en que empiezan las notificaciones diarias.</summary>
    public int DaysBeforeDue { get; set; }

    public bool NotifyFirmStaff { get; set; } = true;

    public bool NotifyClientPortal { get; set; } = true;

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Note { get; set; }

    public int CreatedByUserId { get; set; }
    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual ICollection<RequestReminderDispatch> Dispatches { get; set; } = new List<RequestReminderDispatch>();
}
