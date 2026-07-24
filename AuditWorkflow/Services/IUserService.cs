using AuditWorkflow.Data;
using AuditWorkflow.Models.Auth;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services
{
    public interface IUserService
    {
        Task<UserResponse> Auth(AuthRequest model, AppDbContext context);

        /// <summary>Usuario activo si correo y contraseña coinciden (incluye Firma).</summary>
        Task<User?> FindAuthenticatedUserAsync(AuthRequest model, AppDbContext context);

        /// <summary>Emite JWT, refresh y menú para un usuario ya validado.</summary>
        /// <param name="activeFirmId">Firma activa (claim JWT y refresh).</param>
        /// <param name="preferredActiveCustomerId">Portal: cliente deseado; si no es válido se resuelve automáticamente.</param>
        Task<UserResponse> CreateLoginResponseAsync(User usuario, AppDbContext context, int activeFirmId, int? preferredActiveCustomerId = null, CancellationToken cancellationToken = default);

        /// <summary>Perfil de sesión actual sin rotar refresh ni emitir tokens en el body.</summary>
        Task<UserResponse> BuildSessionResponseAsync(
            User usuario,
            AppDbContext context,
            int activeFirmId,
            int? activeCustomerId,
            DateTime accessExpiresUtc,
            int accessMinutes,
            CancellationToken cancellationToken = default);
    }
}
