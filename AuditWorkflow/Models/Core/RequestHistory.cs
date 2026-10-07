using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class RequestHistory
    {
        [Key]
        public int RequestHistoryId { get; set; }
        public int RequestId { get; set; }
        [ForeignKey("RequestId")]
        public virtual Request Request { get; set; }
        public string StatusAtMoment { get; set; } // El estado al que pasó
        public string Observation { get; set; } // Por qué se rechazó o qué se aprobó
        /// <summary>Si es true, el portal cliente no debe ver esta entrada en el historial general.</summary>
        public bool FirmInternalOnly { get; set; }
        public DateTime ActionDate { get; set; } = ServerClock.Now;
        public int ActionById { get; set; } // Usuario que tomó la decisión
        [ForeignKey("ActionById")]
        public virtual User ActionBy { get; set; }
    }
}
