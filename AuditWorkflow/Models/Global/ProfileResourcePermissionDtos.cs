using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global;

public sealed class ResourcePermissionTripletDto
{
    public bool Create { get; set; }
    public bool Update { get; set; }
    public bool Delete { get; set; }
}

public sealed class ProfileResourcePermissionsDto
{
    public ResourcePermissionTripletDto Teams { get; set; } = new();
    public ResourcePermissionTripletDto Customers { get; set; } = new();
    public ResourcePermissionTripletDto Projects { get; set; } = new();
    public ResourcePermissionTripletDto Requests { get; set; } = new();

    public static ProfileResourcePermissionsDto FullAccess =>
        new()
        {
            Teams = AllTrue(),
            Customers = AllTrue(),
            Projects = AllTrue(),
            Requests = AllTrue()
        };

    private static ResourcePermissionTripletDto AllTrue() =>
        new() { Create = true, Update = true, Delete = true };
}

public sealed class ProfileResourcePermissionsPutInput
{
    [Required]
    public IList<ProfilePermissionRowInput>? Items { get; set; }
}

public sealed class ProfilePermissionRowInput
{
    /// <summary>Código estable: teams, customers, projects, requests.</summary>
    [Required]
    [MaxLength(32)]
    public string Resource { get; set; } = "";

    public bool Create { get; set; }
    public bool Update { get; set; }
    public bool Delete { get; set; }
}
