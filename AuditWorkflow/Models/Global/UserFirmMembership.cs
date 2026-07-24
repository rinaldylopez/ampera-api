using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Models.Global;

/// <summary>Firma(s) a las que un usuario puede acceder (sesión activa vía JWT <c>FirmId</c>).</summary>
public class UserFirmMembership
{
    [Key]
    public int UserFirmMembershipId { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    public int FirmId { get; set; }

    [ForeignKey(nameof(FirmId))]
    public virtual Firm Firm { get; set; } = null!;
}
