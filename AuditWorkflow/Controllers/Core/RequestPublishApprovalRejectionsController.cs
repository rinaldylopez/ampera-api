using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/publish-approval-rejections")]
[ApiController]
public class RequestPublishApprovalRejectionsController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestPublishApprovalRejection>>> GetRejections(
        int requestId,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestPublishApprovalRejections
            .AsNoTracking()
            .Include(r => r.RejectedBy)
            .Include(r => r.RequestedBy)
            .Where(r => r.RequestId == requestId)
            .OrderByDescending(r => r.RejectedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
