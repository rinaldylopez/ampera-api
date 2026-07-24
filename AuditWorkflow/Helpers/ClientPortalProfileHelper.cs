using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Helpers;

/// <summary>
/// Perfil reservado para usuarios portal cliente (<see cref="TenantQueries.UserTypeClient"/>).
/// Debe existir con <see cref="ExpectedProfileId"/> para alinearse con <c>ProfilesController</c>.
/// </summary>
public static class ClientPortalProfileHelper
{
    public const int ExpectedProfileId = 3;
    public const string ProfileName = "Portal cliente";

    /// <summary>
    /// Garantiza que el perfil portal (ID 3) exista; lo crea si falta (BD sin seed completo).
    /// </summary>
    public static async Task<int> EnsureProfileExistsAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        if (await context.Profiles.AsNoTracking()
                .AnyAsync(p => p.ProfileId == ExpectedProfileId, cancellationToken))
            return ExpectedProfileId;

        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await context.Profiles.AsNoTracking()
                    .AnyAsync(p => p.ProfileId == ExpectedProfileId, cancellationToken))
            {
                await tx.CommitAsync(cancellationToken);
                return ExpectedProfileId;
            }

            await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Profiles ON", cancellationToken);
            context.Profiles.Add(new Profile
            {
                ProfileId = ExpectedProfileId,
                Name = ProfileName
            });
            await context.SaveChangesAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT Profiles OFF", cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return ExpectedProfileId;
    }
}
