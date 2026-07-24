using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Services;

public class UserCustomerAccessService : IUserCustomerAccessService
{
    public async Task<bool> CanAccessCustomerAsync(int userId, int customerId, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var u = await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null || u.UserType != TenantQueries.UserTypeClient)
            return false;

        var cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (cust == null || cust.FirmId != u.FirmId)
            return false;

        if (u.CustomerId == customerId)
            return true;

        return await context.UserCustomerMemberships.AsNoTracking().AnyAsync(
            m => m.UserId == userId && m.CustomerId == customerId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<IdNameDto>> GetAccessibleCustomersAsync(int userId, AppDbContext context, CancellationToken cancellationToken = default)
    {
        var u = await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null || u.UserType != TenantQueries.UserTypeClient)
            return [];

        var membershipIds = await context.UserCustomerMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.CustomerId)
            .ToListAsync(cancellationToken);

        var customerIds = membershipIds.ToHashSet();
        if (u.CustomerId is int home)
            customerIds.Add(home);

        return await context.Customers.AsNoTracking()
            .Where(c => c.FirmId == u.FirmId && customerIds.Contains(c.CustomerId))
            .OrderBy(c => c.Name)
            .Select(c => new IdNameDto { Id = c.CustomerId, Name = c.Name })
            .ToListAsync(cancellationToken);
    }

    public async Task SyncMembershipsForClientUserAsync(
        int userId,
        int homeCustomerId,
        IReadOnlyCollection<int> customerIds,
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        var u = await context.Users.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (u == null || u.UserType != TenantQueries.UserTypeClient)
            return;

        var desired = new HashSet<int>(customerIds);
        desired.Add(homeCustomerId);

        foreach (var cid in desired.ToList())
        {
            if (!await context.Customers.AnyAsync(c => c.CustomerId == cid && c.FirmId == u.FirmId, cancellationToken))
                desired.Remove(cid);
        }

        var existing = await context.UserCustomerMemberships
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var row in existing.Where(row => !desired.Contains(row.CustomerId)))
            context.UserCustomerMemberships.Remove(row);

        var have = existing.Select(e => e.CustomerId).ToHashSet();
        foreach (var cid in desired.Where(cid => !have.Contains(cid)))
        {
            context.UserCustomerMemberships.Add(new UserCustomerMembership { UserId = userId, CustomerId = cid });
        }

        await context.SaveChangesAsync(cancellationToken);
        await TenantQueries.PruneIneligibleClientTeamMembershipsAsync(context, userId, cancellationToken);
    }
}
