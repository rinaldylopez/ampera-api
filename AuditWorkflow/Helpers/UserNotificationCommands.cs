using AuditWorkflow.Data;
using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Helpers;

public static class UserNotificationCommands
{
    public static async Task CreateManyAsync(
        AppDbContext db,
        int firmId,
        IEnumerable<int> userIds,
        string title,
        string? body,
        string? linkPath,
        int? requestId,
        byte kind,
        CancellationToken cancellationToken = default)
    {
        var uidList = userIds.Where(u => u > 0).Distinct().ToList();
        if (uidList.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var uid in uidList)
        {
            db.UserNotifications.Add(new UserNotification
            {
                UserId = uid,
                FirmId = firmId,
                Title = title,
                Body = body,
                LinkPath = linkPath,
                RequestId = requestId,
                Kind = kind,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
