using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global
{
    public class Country
    {
        [Key]
        public int CountryId { get; set; }
        [Required]
        [MaxLength(100)]
        public string Name { get; set; }
        [MaxLength(5)]
        public string IsoCode { get; set; } // Ejemplo: SV, US, MX
        [MaxLength(10)]
        public string PhonePrefix { get; set; } = string.Empty; // Ejemplo: +503
    }
}
