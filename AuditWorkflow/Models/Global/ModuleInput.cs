using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global;

/// <summary>
/// Cuerpo para crear/actualizar módulo sin la colección de navegación.
/// </summary>
public class ModuleInput
{
    public int ModuleId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string Icon { get; set; }
}
