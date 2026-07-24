using AuditWorkflow.Models.Core;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Global
{
    public class User
    {
        [Key]
        public int UserId { get; set; }
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; }
        [Required]
        [MaxLength(100)]
        public string Email { get; set; }
        [Required]
        [JsonIgnore]
        public string PasswordHash { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Relación con Perfiles (Roles de UI/Permisos)
        /// </summary>
        public int ProfileId { get; set; }
        [ForeignKey("ProfileId")]
        public virtual Profile Profile { get; set; }

        /// <summary>
        /// Categoría de seguridad
        /// </summary>
        [MaxLength(50)]
        public string SecurityRole { get; set; } // Ver UserSecurityRoles (Admin, Operator, Standard).

        /// <summary>
        /// Clasificación del usuario
        /// </summary>
        public int UserType { get; set; } // 1. Firma, 2. Cliente

        /// <summary>
        /// Siempre pertenece a la Firma (Tenant principal)
        /// </summary>
        public int FirmId { get; set; }
        [ForeignKey("FirmId")]
        public virtual Firm Firm { get; set; }

        /// <summary>
        /// Solo se llena si Type == External
        /// </summary>
        public int? CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; }

        /// <summary>
        /// Puesto del usuario
        /// </summary>
        public int JobTitleId { get; set; }
        [ForeignKey("JobTitleId")]
        public virtual JobTitle JobTitle { get; set; }

        /// <summary>
        /// Guid del usuario
        /// </summary>
        public string UserGuid { get; set; }

        /// <summary>Ruta pública del archivo bajo wwwroot (p. ej. /uploads/profiles/…).</summary>
        [MaxLength(500)]
        public string? ProfilePhotoPath { get; set; }

        /// <summary>Si es true, el usuario debe cambiar la contraseña tras el primer acceso (contraseña provisional).</summary>
        public bool MustChangePassword { get; set; }

        /// <summary>Firmas en las que puede operar (además de <see cref="FirmId"/> como firma “hogar”).</summary>
        public virtual ICollection<UserFirmMembership> UserFirmMemberships { get; set; } = new List<UserFirmMembership>();

        /// <summary>Clientes (empresas) a los que puede acceder en portal; <see cref="CustomerId"/> es el predeterminado.</summary>
        public virtual ICollection<UserCustomerMembership> UserCustomerMemberships { get; set; } = new List<UserCustomerMembership>();
    }
}

