using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Core;

/// <summary>
/// Catálogo de tipos de proyecto por firma (sustituye el enum textual en Projects.EngagementType).
/// </summary>
public class ProjectType
{
    [Key]
    public int ProjectTypeId { get; set; }

    public int FirmId { get; set; }
    [ForeignKey(nameof(FirmId))]
    public virtual Firm Firm { get; set; } = null!;

    /// <summary>Código estable (ej. Financial) para datos y filtros.</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Nombre visible (puede ser bilingüe en un solo campo).</summary>
    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
