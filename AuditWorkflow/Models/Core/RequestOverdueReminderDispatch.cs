using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Bitácora del digest diario de vencidos: un registro por usuario y día
/// (un solo correo agregado, no uno por solicitud).
/// </summary>
public class RequestOverdueReminderDispatch
{
    [Key]
    public int RequestOverdueReminderDispatchId { get; set; }

    /// <summary>Usuario destinatario del digest (firma o portal cliente).</summary>
    public int UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; }

    /// <summary>Día calendario (hora del servidor) en que se envió el digest.</summary>
    public DateTime NotifyOnDate { get; set; }

    public DateTime DispatchedAt { get; set; } = ServerClock.Now;
}
