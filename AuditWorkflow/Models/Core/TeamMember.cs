using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core
{
    public class TeamMember
    {
        [Key]
        public int TeamMemberId { get; set; }
        public int TeamId { get; set; }
        [ForeignKey("TeamId")]
        public virtual Team Team { get; set; }
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        /// <summary>
        /// Útil para saber quién responde por el grupo
        /// </summary>
        public bool IsLeader { get; set; } = false;

        /// <summary>
        /// Fecha de asignación
        /// </summary>
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}
