using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class RequestAttachment
    {
        [Key]
        public int RequestAttachmentId { get; set; }
        public int RequestId { get; set; }
        [ForeignKey("RequestId")]
        public virtual Request Request { get; set; }
        [Required]
        [MaxLength(255)]
        public string FileName { get; set; }
        public string FilePath { get; set; }

        /// <summary>
        /// Datos de trazbilidad
        /// </summary>
        public DateTime UploadedAt { get; set; } = ServerClock.Now;
        public int UploadedById { get; set; }
        [ForeignKey("UploadedById")]
        public virtual User UploadedBy { get; set; }

        /// <summary>
        /// Control de ciclo de vida del archivo
        /// Enum: Pending, Accepted, Rejected, Superseded
        /// </summary>
        public AttachmentStatus FileStatus { get; set; }

        /// <summary>
        /// Nota específica sobre este archivo
        /// </summary>
        public string RejectionReason { get; set; }

        /// <summary>
        /// Control de borrado de archivo
        /// </summary>
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }

        [ForeignKey("DeletedById")]
        public virtual User DeletedBy { get; set; }
    }

    public enum AttachmentStatus
    {
        Pending = 1,   // Subido por el cliente, pendiente de revisión
        Accepted = 2,  // El auditor lo validó
        Rejected = 3,  // El auditor lo rechazó
        Superseded = 4 // Fue reemplazado por una versión más nueva
    }
}
