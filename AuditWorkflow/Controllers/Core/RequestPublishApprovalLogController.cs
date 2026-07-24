using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/publish-approval-log")]
[ApiController]
public class RequestPublishApprovalLogController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    /// <summary>Bitácora interna de firma: envíos solicitados, aprobados, rechazados y cancelados.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetLog(int requestId, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var rows = await _context.RequestPublishApprovalEvents
            .AsNoTracking()
            .Include(e => e.Actor)
            .Include(e => e.Related)
            .Where(e => e.RequestId == requestId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => new
            {
                requestPublishApprovalEventId = e.RequestPublishApprovalEventId,
                eventType = e.EventType,
                occurredAtUtc = e.OccurredAtUtc,
                reason = e.Reason,
                actorUserId = e.ActorUserId,
                actorFullName = e.Actor.FullName,
                relatedUserId = e.RelatedUserId,
                relatedFullName = e.Related != null ? e.Related.FullName : null
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }
}
