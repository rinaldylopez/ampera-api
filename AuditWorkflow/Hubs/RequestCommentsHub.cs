using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Hubs;

[Authorize]
public class RequestCommentsHub(IServiceScopeFactory scopeFactory, ILogger<RequestCommentsHub> logger) : Hub
{
    public static string GroupForRequest(int requestId) => $"request-comments-{requestId}";

    public async Task Subscribe(int requestId)
    {
        if (requestId <= 0)
        {
            await Clients.Caller.SendAsync("realtimeError", "requestId inválido.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var uid = Context.User?.GetUserId();
        var firmId = Context.User?.GetFirmId();
        if (uid is null || firmId is null)
        {
            await Clients.Caller.SendAsync("realtimeError", "No autenticado.");
            return;
        }

        if (!await TenantQueries.RequestAccessibleToUserAsync(db, requestId, uid.Value, firmId.Value, Context.User?.GetCustomerId()))
        {
            logger.LogWarning("Usuario {UserId} intentó suscripción SignalR no autorizada (request {RequestId})", uid, requestId);
            await Clients.Caller.SendAsync("realtimeError", "Sin acceso a esta solicitud.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRequest(requestId));
    }

    public Task Unsubscribe(int requestId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForRequest(requestId));
    }
}
