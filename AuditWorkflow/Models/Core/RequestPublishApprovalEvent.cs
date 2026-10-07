using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core;

/// <summary>Bitácora interna de firma: solicitudes de envío, aprobaciones, rechazos y cancelaciones.</summary>
public class RequestPublishApprovalEvent
{
    [Key]
    public int RequestPublishApprovalEventId { get; set; }

    public int RequestId { get; set; }
    [ForeignKey(nameof(RequestId))]
    public virtual Request Request { get; set; }

    public int EventType { get; set; }

    public int ActorUserId { get; set; }
    [ForeignKey(nameof(ActorUserId))]
    public virtual User Actor { get; set; }

    /// <summary>Usuario relacionado (p. ej. quien solicitó el envío antes de aprobar/rechazar).</summary>
    public int? RelatedUserId { get; set; }
    [ForeignKey(nameof(RelatedUserId))]
    public virtual User Related { get; set; }

    [MaxLength(2000)]
    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; } = ServerClock.Now;
}
