using AuditWorkflow.Models.Global;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Models.Core
{
    public class Request
    {
        [Key]
        public int RequestId { get; set; }

        /// <summary>
        /// Identificador público para URLs. El RequestId queda solo para relaciones internas.
        /// </summary>
        public Guid RequestGuid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Número visible por proyecto (1, 2, 3…). Se reinicia en cada proyecto. RequestId sigue siendo el PK interno.
        /// </summary>
        public int RequestNumber { get; set; }

        /// <summary>Nombre / título de la solicitud en español.</summary>
        [Required]
        [MaxLength(250)]
        public string Title { get; set; }

        /// <summary>Mismo contenido conceptual en inglés (obligatorio al crear/editar desde la firma).</summary>
        [Required]
        [MaxLength(250)]
        public string TitleEn { get; set; } = "";
        /// <summary>
        /// Texto enriquecido (HTML)
        /// </summary>
        public string Description { get; set; } 

        public DateTime? DueDate { get; set; }

        /// <summary>
        /// Enum: Low, Medium, High, Critical
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Enum: Draft (0), Sent (1), SentToClient (2), Rejected (3), Accepted (4), InReviewByFirm (5).
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Flujo portal cliente/firma: <see cref="RequestPortalHandoff"/>.
        /// </summary>
        public int PortalHandoffStatus { get; set; } = (int)RequestPortalHandoff.WithClient;

        /// <summary>
        /// Relación con el Proyecto
        /// </summary>
        public int ProjectId { get; set; }
        [ForeignKey("ProjectId")]
        public virtual Project Project { get; set; }

        /// <summary>
        /// El contacto de la firma responsable
        /// </summary>
        public int FirmContactId { get; set; }
        [ForeignKey("FirmContactId")]
        public virtual User FirmContact { get; set; }

        /// <summary>
        /// Historial
        /// </summary>
        public DateTime CreatedAt { get; set; } = ServerClock.Now;
        public int UserCreatedId { get; set; }

        /// <summary>
        /// Cuándo el cliente envió la documentación a la firma (hora del servidor).
        /// Se limpia si el cliente cancela el envío antes de revisión.
        /// </summary>
        public DateTime? ResponseReceivedAt { get; set; }

        /// <summary>
        /// Cuándo la firma dio por recibida y cerrada la solicitud PBC (solo lectura en portal después).
        /// </summary>
        public DateTime? ClosedAt { get; set; }

        public int? ClosedById { get; set; }
        [ForeignKey("ClosedById")]
        public virtual User ClosedBy { get; set; }

        /// <summary>
        /// <see cref="RequestPublishToClientApproval"/>: ninguno, pendiente manager/socio, o rechazado por manager/socio.
        /// </summary>
        public int PublishToClientApprovalState { get; set; }

        /// <summary>Último motivo de rechazo de envío (solo en listados; no persistido).</summary>
        [NotMapped]
        public string? LastPublishRejectionReason { get; set; }

        /// <summary>Último rechazo de envío (solo en listados; no persistido).</summary>
        [NotMapped]
        public DateTime? LastPublishRejectionAtUtc { get; set; }

        /// <summary>Quién rechazó el envío (solo en listados; no persistido).</summary>
        [NotMapped]
        public string? LastPublishRejectionActorName { get; set; }

        /// <summary>Última publicación al portal del cliente (solo en listados; desde bitácora).</summary>
        [NotMapped]
        public DateTime? PublishedToClientAtUtc { get; set; }

        public DateTime? PublishApprovalRequestedUtc { get; set; }

        public int? PublishApprovalRequestedByUserId { get; set; }
        [ForeignKey(nameof(PublishApprovalRequestedByUserId))]
        public virtual User PublishApprovalRequestedBy { get; set; }

        /// <summary>
        /// Colecciones para asignaciones y archivos
        /// </summary>
        public virtual ICollection<RequestAssignment> Assignments { get; set; }
        public virtual ICollection<RequestAttachment> Attachments { get; set; }
        public virtual ICollection<RequestProvidedFile> ProvidedFiles { get; set; }
        public virtual ICollection<RequestComment> Comments { get; set; }
    }
}
