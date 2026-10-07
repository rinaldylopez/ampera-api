using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core;

/// <summary>Evita duplicar la notificación de un recordatorio en el mismo día calendario (UTC).</summary>
public class RequestReminderDispatch
{
    [Key]
    public int RequestReminderDispatchId { get; set; }

    public int RequestReminderId { get; set; }
    [ForeignKey(nameof(RequestReminderId))]
    public virtual RequestReminder RequestReminder { get; set; }

    /// <summary>Día calendario (UTC) en que se envió la notificación.</summary>
    public DateTime NotifyOnDate { get; set; }

    public DateTime DispatchedAtUtc { get; set; } = ServerClock.Now;
}
