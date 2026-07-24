using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global;

public class ProfileInput
{
    public int ProfileId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = "";
}
