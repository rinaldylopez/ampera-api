using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services;

public interface IUserFirmAccessService
{
    /// <summary>El usuario puede usar <paramref name="firmId"/> como firma activa de sesión.</summary>
    Task<bool> CanAccessFirmAsync(int userId, int firmId, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>Firmas a las que el usuario tiene acceso (para selector / CRUD).</summary>
    Task<IReadOnlyList<IdNameDto>> GetAccessibleFirmsAsync(int userId, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>Sincroniza membresías para usuarios tipo firma. Siempre incluye <paramref name="homeFirmId"/>.</summary>
    Task SyncMembershipsForFirmUserAsync(int userId, int homeFirmId, IReadOnlyCollection<int> firmIds, AppDbContext context, CancellationToken cancellationToken = default);
}
