using System.Security.Claims;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services;

public interface IProfileResourcePermissionService
{
    /// <summary>Si el usuario es portal cliente, siempre true. Admin de plataforma: true.</summary>
    Task<bool> AllowsFirmMutationAsync(
        ClaimsPrincipal user,
        ProfileProtectedResource resource,
        ResourcePermissionAction action,
        CancellationToken cancellationToken = default);

    /// <summary>DTO efectivo para el login (considera Admin y matrices del perfil).</summary>
    Task<ProfileResourcePermissionsDto> ResolveDtoForLoginAsync(int profileId, bool isSecurityAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>Matriz persistida para edición del perfil (sin bypass por Admin).</summary>
    Task<ProfileResourcePermissionsDto> GetMatrixForProfileEditorAsync(int profileId,
        CancellationToken cancellationToken = default);
}
