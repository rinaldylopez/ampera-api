using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class RequestProvidedFile
    {
        [Key]
        public int RequestProvidedFileId { get; set; }

        public int RequestId { get; set; }
        [ForeignKey("RequestId")]
        public virtual Request Request { get; set; }

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; }

        [Required]
        public string FilePath { get; set; }

        public DateTime UploadedAt { get; set; } = ServerClock.Now;

        public int UploadedById { get; set; }
        [ForeignKey("UploadedById")]
        public virtual User UploadedBy { get; set; }

        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public int? DeletedById { get; set; }

        [ForeignKey("DeletedById")]
        public virtual User DeletedBy { get; set; }
    }
}
