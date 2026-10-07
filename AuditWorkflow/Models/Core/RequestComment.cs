using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class RequestComment
    {
        [Key]
        public int RequestCommentId { get; set; }
        public int RequestId { get; set; }
        [ForeignKey("RequestId")]
        public virtual Request Request { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        [Required]
        public string CommentText { get; set; }

        public DateTime CreatedAt { get; set; } = ServerClock.Now;

        // --- NUEVOS CAMPOS PARA MULTIMEDIA ---

        /// <summary>
        /// URL o Path del archivo (Imagen o Audio)
        /// </summary>
        public string AttachmentUrl { get; set; }

        /// <summary>
        /// Para saber qué renderizar en el Front-end (Ej: "image", "audio", "text")
        /// </summary>
        [MaxLength(20)]
        public string AttachmentType { get; set; }

        /// <summary>
        /// Si ya fue leido
        /// </summary>
        public bool IsRead { get; set; } = false;
    }
}
