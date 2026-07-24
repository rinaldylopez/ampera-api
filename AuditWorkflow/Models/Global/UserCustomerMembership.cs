using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Models.Global;

/// <summary>Cliente(s) de empresa a los que puede acceder un usuario portal (<see cref="User.UserType"/> cliente).</summary>
public class UserCustomerMembership
{
    [Key]
    public int UserCustomerMembershipId { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User User { get; set; } = null!;

    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer Customer { get; set; } = null!;
}
