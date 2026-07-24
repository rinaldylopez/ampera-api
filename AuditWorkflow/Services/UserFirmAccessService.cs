using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Services;

public class UserFirmAccessService : IUserFirmAccessService
{
    public async Task<bool> CanAccessFirmAsync(int userId, int firmId, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var u = await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null) return false;

        if (u.UserType == TenantQueries.UserTypeClient)
            return u.FirmId == firmId;

        return await context.UserFirmMemberships.AsNoTracking().AnyAsync(
            m => m.UserId == userId && m.FirmId == firmId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<IdNameDto>> GetAccessibleFirmsAsync(int userId, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var u = await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null) return [];

        if (u.UserType == TenantQueries.UserTypeClient)
        {
            var name = await context.Firms.AsNoTracking()
                .Where(f => f.FirmId == u.FirmId)
                .Select(f => f.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "";
            return new List<IdNameDto> { new() { Id = u.FirmId, Name = name } };
        }

        return await context.UserFirmMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(context.Firms.AsNoTracking(), m => m.FirmId, f => f.FirmId, (_, f) => new IdNameDto { Id = f.FirmId, Name = f.Name })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task SyncMembershipsForFirmUserAsync(
        int userId,
        int homeFirmId,
        IReadOnlyCollection<int> firmIds,
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var u = await context.Users.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null || u.UserType != TenantQueries.UserTypeFirm) return;

        var desired = new HashSet<int>(firmIds);
        desired.Add(homeFirmId);

        var existing = await context.UserFirmMemberships
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var row in existing.Where(row => !desired.Contains(row.FirmId)))
            context.UserFirmMemberships.Remove(row);

        var have = existing.Select(e => e.FirmId).ToHashSet();
        foreach (var fid in desired.Where(fid => !have.Contains(fid)))
        {
            if (!await context.Firms.AnyAsync(f => f.FirmId == fid, cancellationToken))
                continue;
            context.UserFirmMemberships.Add(new UserFirmMembership { UserId = userId, FirmId = fid });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
