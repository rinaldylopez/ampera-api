using AuditWorkflow.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Helpers;

public static class RequestNotificationRecipients
{
    public static async Task<IReadOnlyList<int>> GetClientPortalUserIdsAsync(
        AppDbContext db,
        int firmId,
        int customerId,
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<int>();

        var portalIds = await db.Users.AsNoTracking()
            .Where(u => u.FirmId == firmId
                        && u.UserType == TenantQueries.UserTypeClient
                        && u.IsActive
                        && (u.CustomerId == customerId
                            || db.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)))
            .Select(u => u.UserId)
            .ToListAsync(cancellationToken);
        foreach (var pid in portalIds)
            ids.Add(pid);

        var assigns = await db.RequestAssignments.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .Select(a => new { a.TeamId, a.UserId })
            .ToListAsync(cancellationToken);

        foreach (var a in assigns)
        {
            if (a.UserId is int uid && uid > 0)
            {
                if (await db.Users.AsNoTracking().AnyAsync(
                        u => u.UserId == uid
                             && u.FirmId == firmId
                             && u.UserType == TenantQueries.UserTypeClient
                             && u.IsActive
                             && (u.CustomerId == customerId
                                 || db.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)),
                        cancellationToken))
                    ids.Add(uid);
            }

            if (a.TeamId is int tid && tid > 0)
            {
                var memberIds = await db.TeamMembers.AsNoTracking()
                    .Where(tm => tm.TeamId == tid)
                    .Select(tm => tm.UserId)
                    .ToListAsync(cancellationToken);
                foreach (var mid in memberIds)
                {
                    if (await db.Users.AsNoTracking().AnyAsync(
                            u => u.UserId == mid
                                 && u.FirmId == firmId
                                 && u.UserType == TenantQueries.UserTypeClient
                                 && u.IsActive
                                 && (u.CustomerId == customerId
                                     || db.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)),
                            cancellationToken))
                        ids.Add(mid);
                }
            }
        }

        return ids.ToList();
    }

    public static async Task<IReadOnlyList<int>> GetFirmStaffUserIdsAsync(
        AppDbContext db,
        int firmId,
        int requestId,
        int projectId,
        int firmContactUserId,
        CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<int>();
        if (firmContactUserId > 0 &&
            await db.Users.AsNoTracking().AnyAsync(
                u => u.UserId == firmContactUserId && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                cancellationToken))
            ids.Add(firmContactUserId);

        var mgrId = await db.Projects.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => (int?)p.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (mgrId is int m && m > 0 &&
            await db.Users.AsNoTracking().AnyAsync(
                u => u.UserId == m && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                cancellationToken))
            ids.Add(m);

        var partnerId = await db.Projects.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => p.PartnerInChargeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (partnerId is int pId && pId > 0 &&
            await db.Users.AsNoTracking().AnyAsync(
                u => u.UserId == pId && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                cancellationToken))
            ids.Add(pId);

        var staffIds = await db.ProjectStaffs.AsNoTracking()
            .Where(ps => ps.ProjectId == projectId)
            .Select(ps => ps.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var sid in staffIds)
        {
            if (sid > 0 &&
                await db.Users.AsNoTracking().AnyAsync(
                    u => u.UserId == sid && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                    cancellationToken))
                ids.Add(sid);
        }

        var assigns = await db.RequestAssignments.AsNoTracking()
            .Where(a => a.RequestId == requestId)
            .Select(a => new { a.TeamId, a.UserId })
            .ToListAsync(cancellationToken);
        foreach (var a in assigns)
        {
            if (a.UserId is int uid && uid > 0 &&
                await db.Users.AsNoTracking().AnyAsync(
                    u => u.UserId == uid && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                    cancellationToken))
                ids.Add(uid);

            if (a.TeamId is int tid && tid > 0)
            {
                var memberIds = await db.TeamMembers.AsNoTracking()
                    .Where(tm => tm.TeamId == tid)
                    .Select(tm => tm.UserId)
                    .ToListAsync(cancellationToken);
                foreach (var mid in memberIds)
                {
                    if (mid > 0 &&
                        await db.Users.AsNoTracking().AnyAsync(
                            u => u.UserId == mid && u.FirmId == firmId && u.UserType == TenantQueries.UserTypeFirm && u.IsActive,
                            cancellationToken))
                        ids.Add(mid);
                }
            }
        }

        return ids.ToList();
    }

    public static async Task<IReadOnlyList<string>> GetEmailsForUserIdsAsync(
        AppDbContext db,
        IEnumerable<int> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(u => u > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<string>();

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.UserId) && u.Email != null && u.Email.Trim() != "")
            .Select(u => u.Email.Trim())
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
