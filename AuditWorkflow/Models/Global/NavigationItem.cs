using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Global
{
    public class NavigationItem
    {
        [Key]
        public int NavigationItemId { get; set; }
        [Required]
        [MaxLength(100)]
        public string Name { get; set; }
        [Required]
        [MaxLength(100)]
        public string Path { get; set; }
        public int ModuleId { get; set; }
        [ForeignKey("ModuleId")]
        [JsonIgnore]
        public virtual Module Module { get; set; }
    }
}
