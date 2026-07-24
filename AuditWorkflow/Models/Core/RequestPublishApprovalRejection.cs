using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Bitácora cuando manager/socio rechaza el envío al cliente tras una solicitud de aprobación.
/// </summary>
public class RequestPublishApprovalRejection
{
    [Key]
    public int RequestPublishApprovalRejectionId { get; set; }

    public int RequestId { get; set; }
    [ForeignKey(nameof(RequestId))]
    public virtual Request Request { get; set; }

    public int RejectedByUserId { get; set; }
    [ForeignKey(nameof(RejectedByUserId))]
    public virtual User RejectedBy { get; set; }

    /// <summary>Usuario que había solicitado la aprobación de envío (si aplica).</summary>
    public int? RequestedByUserId { get; set; }
    [ForeignKey(nameof(RequestedByUserId))]
    public virtual User RequestedBy { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = "";

    public DateTime RejectedAtUtc { get; set; } = DateTime.UtcNow;
}
