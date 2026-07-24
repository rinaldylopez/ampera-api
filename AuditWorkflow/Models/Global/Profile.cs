using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Global
{
    public class Profile
    {
        [Key]
        public int ProfileId { get; set; }
        [Required]
        [MaxLength(50)]
        public string Name { get; set; }
        [JsonIgnore]
        public virtual ICollection<ProfileItem> ProfileItems { get; set; }
    }
}
