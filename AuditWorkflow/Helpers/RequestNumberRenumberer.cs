using AuditWorkflow.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Helpers;

/// <summary>
/// Reasigna RequestNumber de borradores tras eliminaciones (sin huecos entre borradores).
/// Las solicitudes ya publicadas o en trámite (Status != 0) conservan su número.
/// </summary>
public static class RequestNumberRenumberer
{
    public static async Task RenumberDraftsInProjectAsync(
        AppDbContext context,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Requests
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.RequestNumber)
            .ThenBy(r => r.RequestId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return;

        var taken = rows
            .Where(r => r.Status != 0)
            .Select(r => r.RequestNumber)
            .ToHashSet();

        var drafts = rows.Where(r => r.Status == 0).ToList();
        if (drafts.Count == 0)
            return;

        var next = 1;
        var changed = false;
        foreach (var draft in drafts)
        {
            while (taken.Contains(next))
                next++;

            if (draft.RequestNumber != next)
            {
                draft.RequestNumber = next;
                changed = true;
            }

            taken.Add(next);
            next++;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }
}
