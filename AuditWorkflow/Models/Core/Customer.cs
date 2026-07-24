using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core
{
    public class Customer
    {
        [Key]
        public int CustomerId { get; set; }
        [Required]
        [MaxLength(250)]
        public string Name { get; set; }
        [MaxLength(50)]
        public string TaxId { get; set; } // NIT del cliente
        [MaxLength(50)]
        public string RegistrationNumber { get; set; } // NRC del cliente
        [MaxLength(500)]
        public string Address { get; set; }
        [Required]
        [MaxLength(20)]
        public string Category { get; set; } = "Otros";
        [MaxLength(200)]
        public string BusinessLine { get; set; }

        /// <summary>
        /// Actividad económica (catálogo global). Si está asignada, <see cref="BusinessLine"/> puede mantener el nombre por compatibilidad.
        /// </summary>
        public int? EconomicActivityId { get; set; }
        [ForeignKey("EconomicActivityId")]
        public virtual EconomicActivity EconomicActivity { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Datos de contacto del cliente
        /// </summary>
        public int CountryId { get; set; }
        [ForeignKey("CountryId")]
        public virtual Country Country { get; set; }
        [MaxLength(150)]
        public string Email { get; set; }
        public int? PhoneCountryId { get; set; }
        [ForeignKey("PhoneCountryId")]
        public virtual Country PhoneCountry { get; set; }
        [MaxLength(50)]
        public string Phone { get; set; }

        /// <summary>
        /// Relación con la Firma dueña de este cliente
        /// </summary>
        public int FirmId { get; set; }
        [ForeignKey("FirmId")]
        public virtual Firm Firm { get; set; }

        public virtual ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
    }
}
