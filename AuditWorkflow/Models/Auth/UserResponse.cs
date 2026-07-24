using System.ComponentModel.DataAnnotations;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Models.Auth
{
    public class UserResponse
    {
        [Key]
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public int ProfileId { get; set; }
        public int Expires { get; set; }
        public DateTime ExpirationTime { get; set; }
        /// <summary>Firma activa de la sesión (coincide con el claim JWT FirmId).</summary>
        public int FirmId { get; set; }
        public string FirmName { get; set; }

        /// <summary>Firmas entre las que este usuario puede cambiar (selector).</summary>
        public List<IdNameDto> AccessibleFirms { get; set; } = [];
        /// <summary>Empresas cliente entre las que este usuario portal puede cambiar (selector).</summary>
        public List<IdNameDto> AccessibleCustomers { get; set; } = [];
        /// <summary>1 = firma, 2 = cliente (portal).</summary>
        public int UserType { get; set; } = 1;
        /// <summary>Si <see cref="UserType"/> es cliente, empresa activa de la sesión (claim JWT).</summary>
        public int? CustomerId { get; set; }
        /// <summary>Rol de seguridad (p. ej. Admin) para la UI; el token ya incluye ClaimTypes.Role.</summary>
        public string SecurityRole { get; set; }
        public string RefreshToken { get; set; }
        public string UserToken { get; set; }
        public string? ProfilePhotoPath { get; set; }
        /// <summary>True si debe cambiar la contraseña provisional antes de usar la plataforma.</summary>
        public bool MustChangePassword { get; set; }

        /// <summary>Permisos de mutación por módulo para usuarios de firma (no aplica a portal como matriz independiente).</summary>
        public ProfileResourcePermissionsDto ResourcePermissions { get; set; } = ProfileResourcePermissionsDto.FullAccess;

        public ICollection<Menu> Menu { get; set; }
    }
}
