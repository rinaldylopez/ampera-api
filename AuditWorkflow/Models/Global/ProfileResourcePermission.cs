
namespace AuditWorkflow.Models.Global;

public class ProfileResourcePermission
{
    public int ProfileResourcePermissionId { get; set; }
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    public ProfileProtectedResource Resource { get; set; }

    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
}
