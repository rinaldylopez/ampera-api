using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core
{
    public class Team
    {
        [Key]
        public int TeamId { get; set; }

        /// <summary>
        /// Ej: "Equipo Auditoría Fiscal" o "Contabilidad Cliente X"
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } 

        [MaxLength(250)]
        public string Description { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// La firma siempre es dueña del registro (Tenant)
        /// </summary>
        public int FirmId { get; set; }
        [ForeignKey("FirmId")]
        public virtual Firm Firm { get; set; }

        // Si es null, el equipo es de la Firma. 
        // Si tiene ID, es un equipo que pertenece a un Cliente específico.
        public int? CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; }

        /// <summary>
        /// Relación con los integrantes del equipo
        /// </summary>
        public virtual ICollection<TeamMember> TeamMembers { get; set; }
    }
}
