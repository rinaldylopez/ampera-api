using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global;

/// <summary>
/// Catálogo global de actividades económicas (compartido por todas las firmas).
/// </summary>
public class EconomicActivity
{
    [Key]
    public int EconomicActivityId { get; set; }

    [Required]
    [MaxLength(32)]
    public string Code { get; set; }

    [Required]
    [MaxLength(500)]
    public string Name { get; set; }
}
