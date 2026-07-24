using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core
{
    public class ProjectTeam
    {
        [Key]
        public int ProjectTeamId { get; set; }

        /// <summary>
        /// Proyecto
        /// </summary>
        public int ProjectId { get; set; }
        [ForeignKey("ProjectId")]
        public virtual Project Project { get; set; }

        /// <summary>
        /// El Team ya sabe si es del cliente o de la firma
        /// </summary>
        public int TeamId { get; set; } 
        [ForeignKey("TeamId")]
        public virtual Team Team { get; set; }
    }
}
