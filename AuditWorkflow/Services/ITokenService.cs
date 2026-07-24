using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services;

public interface ITokenService
{
    /// <param name="activeFirmId">Claim JWT <c>FirmId</c> y firma de sesión del refresh token.</param>
    /// <param name="activeCustomerId">Solo portal cliente: claim <c>CustomerId</c> y columna <c>ActiveCustomerId</c> en refresh.</param>
    Task<object> GetTokenAsync(User user, AppDbContext context, int activeFirmId, int? activeCustomerId = null);
}
