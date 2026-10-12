using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Notificación persistida en campana del usuario (portal cliente o firma).
/// </summary>
public class UserNotification
{
    [Key]
    public int UserNotificationId { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; }

    public int FirmId { get; set; }

    [ForeignKey(nameof(FirmId))]
    public virtual Firm Firm { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; }

    [MaxLength(1000)]
    public string? Body { get; set; }

    /// <summary>Ruta SPA relativa, p. ej. /portal/requests/{guid} o /requests/{guid}</summary>
    [MaxLength(500)]
    public string? LinkPath { get; set; }

    /// <summary>1 publicada al cliente, 2 cliente envió a firma, 3 firma devolvió al cliente.</summary>
    public byte Kind { get; set; }

    public int? RequestId { get; set; }

    [ForeignKey(nameof(RequestId))]
    public virtual Request? Request { get; set; }

    public DateTime CreatedAt { get; set; } = ServerClock.Now;

    public DateTime? ReadAt { get; set; }
}
