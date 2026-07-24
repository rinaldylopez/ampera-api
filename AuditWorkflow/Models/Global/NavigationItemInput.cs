using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global;

public class NavigationItemInput
{
    public int NavigationItemId { get; set; }
    public int ModuleId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string Path { get; set; } = "";
}
