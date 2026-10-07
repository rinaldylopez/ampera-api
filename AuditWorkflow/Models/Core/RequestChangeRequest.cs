using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Solicitud del portal cliente para ampliar plazo y/o pedir modificación de un PBC ya publicado.
/// La firma aprueba o rechaza; al aprobar una extensión se aplica <see cref="ProposedDueDate"/>.
/// </summary>
public class RequestChangeRequest
{
    [Key]
    public int RequestChangeRequestId { get; set; }

    public int RequestId { get; set; }
    [ForeignKey(nameof(RequestId))]
    public virtual Request Request { get; set; }

    public int RequestedByUserId { get; set; }
    [ForeignKey(nameof(RequestedByUserId))]
    public virtual User RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; } = ServerClock.Now;

    /// <summary><see cref="RequestChangeRequestKinds"/>.</summary>
    public int Kind { get; set; }

    /// <summary>Nueva fecha propuesta (obligatoria si el kind incluye extensión).</summary>
    public DateTime? ProposedDueDate { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = "";

    /// <summary><see cref="RequestChangeRequestStatuses"/>.</summary>
    public int Status { get; set; } = RequestChangeRequestStatuses.Pending;

    public int? ReviewedByUserId { get; set; }
    [ForeignKey(nameof(ReviewedByUserId))]
    public virtual User ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(2000)]
    public string? ReviewNote { get; set; }

    /// <summary>DueDate anterior al aprobar (auditoría).</summary>
    public DateTime? PreviousDueDate { get; set; }
}
