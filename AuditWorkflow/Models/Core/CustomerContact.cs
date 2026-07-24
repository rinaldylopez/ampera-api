using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Core
{
    public class CustomerContact
    {
        [Key]
        public int CustomerContactId { get; set; }

        public int CustomerId { get; set; }
        [ForeignKey(nameof(CustomerId))]
        [JsonIgnore]
        public virtual Customer Customer { get; set; }

        public int CustomerContactTypeId { get; set; }
        [ForeignKey(nameof(CustomerContactTypeId))]
        public virtual CustomerContactType ContactType { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public int? PhoneCountryId { get; set; }
        [ForeignKey(nameof(PhoneCountryId))]
        public virtual Country PhoneCountry { get; set; }

        [MaxLength(50)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;
    }
}
