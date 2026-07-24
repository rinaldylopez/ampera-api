using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global
{
    public class JobTitle
    {
        [Key]
        public int JobTitleId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } // Ejemplo: Manager, Senior Auditor, Engineer

        [MaxLength(250)]
        public string Description { get; set; }
    }
}
