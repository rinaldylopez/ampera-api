using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services;

public interface IUserCustomerAccessService
{
    /// <summary>El usuario portal puede usar <paramref name="customerId"/> como cliente activo de sesión.</summary>
    Task<bool> CanAccessCustomerAsync(int userId, int customerId, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>Empresas cliente a las que el usuario portal tiene acceso (selector / CRUD).</summary>
    Task<IReadOnlyList<IdNameDto>> GetAccessibleCustomersAsync(int userId, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>Sincroniza membresías para usuarios tipo cliente. Siempre incluye <paramref name="homeCustomerId"/>.</summary>
    Task SyncMembershipsForClientUserAsync(int userId, int homeCustomerId, IReadOnlyCollection<int> customerIds, AppDbContext context, CancellationToken cancellationToken = default);
}
