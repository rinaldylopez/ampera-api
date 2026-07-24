using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Core
{
    public class CustomerContactType
    {
        [Key]
        public int CustomerContactTypeId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
