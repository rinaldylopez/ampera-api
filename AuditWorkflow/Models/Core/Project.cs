using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class Project
    {
        [Key]
        public int ProjectId { get; set; }

        /// <summary>
        /// Ej: AUD-2026-001
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ProjectCode { get; set; }

        [Required]
        [MaxLength(250)]
        public string Name { get; set; }

        /// <summary>
        /// Ej: Active, Completed, Paused, Archived
        /// </summary>
        [MaxLength(50)]
        public string Status { get; set; } = "Active";

        /// <summary>
        /// Año fiscal (ej. 2026)
        /// </summary>
        [Required]
        public int AuditPeriod { get; set; }

        /// <summary>
        /// Referencia al catálogo de tipos de proyecto de la firma (sustituye el campo textual histórico).
        /// </summary>
        public int? ProjectTypeId { get; set; }
        [ForeignKey(nameof(ProjectTypeId))]
        public virtual ProjectType? ProjectType { get; set; }

        public DateTime CreatedAt { get; set; } = ServerClock.Now;
        /// <summary>
        /// Periodo en fechas que durara la auditoria
        /// </summary>
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// El dueño del proyecto (Tenant)
        /// </summary>
        public int FirmId { get; set; }
        [ForeignKey("FirmId")]
        public virtual Firm Firm { get; set; }

        /// <summary>
        /// El cliente auditado
        /// </summary>
        public int CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; }

        /// <summary>
        /// Encargado principal (Project Manager de la firma)
        /// </summary>
        public int ManagerId { get; set; }
        [ForeignKey("ManagerId")]
        public virtual User Manager { get; set; }

        /// <summary>
        /// Socio a cargo del encargo (usuario de la firma; opcional).
        /// </summary>
        public int? PartnerInChargeId { get; set; }
        [ForeignKey(nameof(PartnerInChargeId))]
        public virtual User? PartnerInCharge { get; set; }

        /// <summary>
        /// Horas prosupuestadas
        /// </summary>
        public decimal BudgetedHours { get; set; }

        /// <summary>
        /// Valor sugerido al crear recordatorios: días antes de la fecha de vencimiento del recordatorio (no de la solicitud).
        /// </summary>
        public int? DefaultReminderDaysBeforeDue { get; set; }

        /// <summary>
        /// Equipos asignados (pueden ser del cliente o de la firma)
        /// </summary>
        public virtual ICollection<ProjectTeam> ProjectTeams { get; set; }

        /// <summary>
        /// Usuarios individuales de la firma asignados directamente
        /// </summary>
        public virtual ICollection<ProjectStaff> ProjectStaffs { get; set; }
    }
}
