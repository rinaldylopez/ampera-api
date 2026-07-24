using AuditWorkflow.Data;
using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Helpers;

public static class RequestPublishApprovalEventLog
{
    public static void Record(
        AppDbContext db,
        int requestId,
        int eventType,
        int actorUserId,
        int? relatedUserId = null,
        string? reason = null)
    {
        db.RequestPublishApprovalEvents.Add(new RequestPublishApprovalEvent
        {
            RequestId = requestId,
            EventType = eventType,
            ActorUserId = actorUserId,
            RelatedUserId = relatedUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
