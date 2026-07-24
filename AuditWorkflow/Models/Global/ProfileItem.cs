using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Global
{
    public class ProfileItem
    {
        [Key]
        public int ProfileItemId { get; set; }
        public int ProfileId { get; set; }
        [ForeignKey("ProfileId")]
        [JsonIgnore]
        public virtual Profile Profile { get; set; }
        public int NavigationItemId { get; set; }
        [ForeignKey("NavigationItemId")]
        [JsonIgnore]
        public virtual NavigationItem NavigationItem { get; set; }
    }
}
