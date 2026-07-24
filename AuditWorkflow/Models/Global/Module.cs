using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global
{
    public class Module
    {
        [Key]
        public int ModuleId { get; set; }
        [Required]
        [MaxLength(50)]
        public string Name { get; set; }
        [MaxLength(50)]
        public string Icon { get; set; } = "";
        public virtual ICollection<NavigationItem> NavigationItems { get; set; }
    }
}
