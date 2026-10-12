using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Auth;

/// <summary>
/// Enlace de restablecimiento de un solo uso (solo se guarda el hash del token).
/// </summary>
public class PasswordResetToken
{
    [Key]
    public int PasswordResetTokenId { get; set; }

    public int UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; }

    /// <summary>SHA-256 hex del token en claro + pepper de aplicación.</summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    /// <summary>Admin que solicitó el envío (si aplica).</summary>
    public int? RequestedByUserId { get; set; }

    public int FailedAttempts { get; set; }
}
