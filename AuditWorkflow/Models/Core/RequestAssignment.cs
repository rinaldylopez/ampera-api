using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditWorkflow.Models.Core
{
    public class RequestAssignment
    {
        [Key]
        public int RequestAssignmentId { get; set; }
        public int RequestId { get; set; }
        [ForeignKey("RequestId")]
        public virtual Request Request { get; set; }
        /// <summary>
        /// Puede asignarse a un equipo entero del cliente...
        /// </summary>
        public int? TeamId { get; set; }
        [ForeignKey("TeamId")]
        public virtual Team Team { get; set; }

        /// <summary>
        /// ...o a un usuario específico del cliente
        /// </summary>
        public int? UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}
