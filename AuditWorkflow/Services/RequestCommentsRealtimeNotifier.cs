using AuditWorkflow.Hubs;
using AuditWorkflow.Models.Core;
using Microsoft.AspNetCore.SignalR;

namespace AuditWorkflow.Services;

public interface IRequestCommentsRealtimeNotifier
{
    Task NotifyCommentAdded(int requestId, RequestComment persistedWithUser);

    Task NotifyCommentRemoved(int requestId, int requestCommentId);
}

public sealed class RequestCommentsRealtimeNotifier(IHubContext<RequestCommentsHub> hub) : IRequestCommentsRealtimeNotifier
{
    public Task NotifyCommentAdded(int requestId, RequestComment persistedWithUser)
        => hub.Clients.Group(RequestCommentsHub.GroupForRequest(requestId)).SendAsync("commentAdded", ToPayload(persistedWithUser));

    public Task NotifyCommentRemoved(int requestId, int requestCommentId)
        => hub.Clients.Group(RequestCommentsHub.GroupForRequest(requestId)).SendAsync("commentRemoved", new { commentId = requestCommentId });

    /// <summary>Objeto estable en camelCase esperado por el front (similar al GET).</summary>
    private static object ToPayload(RequestComment c)
    {
        var u = c.User;
        return new
        {
            requestCommentId = c.RequestCommentId,
            requestId = c.RequestId,
            commentText = c.CommentText,
            createdAt = c.CreatedAt,
            attachmentUrl = c.AttachmentUrl,
            attachmentType = c.AttachmentType,
            user = u == null
                ? null
                : (object)new
                {
                    userId = u.UserId,
                    fullName = u.FullName,
                    email = u.Email,
                    userType = u.UserType,
                    profilePhotoPath = u.ProfilePhotoPath
                }
        };
    }
}
