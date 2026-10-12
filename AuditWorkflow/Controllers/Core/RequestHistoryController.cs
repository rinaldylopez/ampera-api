using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/history")]
[ApiController]
public class RequestHistoryController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestHistory>>> GetHistory(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var isClientPortal = await TenantQueries.UserIsClientPortalAsync(_context, userId.Value);

        var query = _context.RequestHistory
            .AsNoTracking()
            .Include(h => h.ActionBy)
            .Where(h => h.RequestId == requestId);

        if (isClientPortal)
            query = query.Where(h => !h.FirmInternalOnly);

        return await query
            .OrderByDescending(h => h.ActionDate)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<RequestHistory>> PostHistory(int requestId, [FromBody] RequestHistory entry)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))
            return NotFound();
        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");
        if (!await TenantQueries.UserBelongsToFirmAsync(_context, userId.Value, firmId.Value))
            return BadRequest();

        entry.RequestId = requestId;
        entry.ActionById = userId.Value;
        entry.ActionDate = ServerClock.Now;
        entry.Request = null;
        entry.ActionBy = null;
        _context.RequestHistory.Add(entry);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetHistory), new { requestId }, entry);
    }
}
