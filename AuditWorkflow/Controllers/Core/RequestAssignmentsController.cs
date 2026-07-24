using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/assignments")]
[ApiController]
public class RequestAssignmentsController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestAssignment>>> GetAssignments(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestAssignments
            .AsNoTracking()
            .Include(a => a.Team)!.ThenInclude(t => t!.TeamMembers)!.ThenInclude(tm => tm.User)
            .Include(a => a.User)
            .Where(a => a.RequestId == requestId)
            .ToListAsync();
    }

    public class AssignmentInput
    {
        public int? TeamId { get; set; }
        public int? UserId { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<RequestAssignment>> PostAssignment(int requestId, [FromBody] AssignmentInput input)
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
        if (input.TeamId is null && input.UserId is null)
            return BadRequest("Debe indicar TeamId o UserId.");
        if (input.TeamId is int tid && !await TenantQueries.TeamBelongsToFirmAsync(_context, tid, firmId.Value))
            return BadRequest("Equipo inválido.");
        if (input.UserId is int uid)
        {
            var requestCustomerId = await _context.Requests
                .AsNoTracking()
                .Where(r => r.RequestId == requestId && r.Project.FirmId == firmId.Value)
                .Select(r => r.Project.CustomerId)
                .FirstOrDefaultAsync();
            var userIsFirmMember = await TenantQueries.UserBelongsToFirmAsync(_context, uid, firmId.Value);
            var userIsCustomerMember = requestCustomerId > 0 &&
                await TenantQueries.UserBelongsToCustomerAsync(_context, uid, firmId.Value, requestCustomerId);
            if (!userIsFirmMember && !userIsCustomerMember)
                return BadRequest("Usuario inválido.");
        }

        var assignedTeamName = input.TeamId is int teamId
            ? await _context.Teams.AsNoTracking()
                .Where(t => t.TeamId == teamId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync()
            : null;
        var assignedUserName = input.UserId is int assignedUserId
            ? await _context.Users.AsNoTracking()
                .Where(u => u.UserId == assignedUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync()
            : null;

        var row = new RequestAssignment
        {
            RequestId = requestId,
            TeamId = input.TeamId,
            UserId = input.UserId
        };
        _context.RequestAssignments.Add(row);
        if (!string.IsNullOrWhiteSpace(assignedTeamName))
            RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.Assignment, $"Se asignó el equipo \"{assignedTeamName}\" a la solicitud.");
        if (!string.IsNullOrWhiteSpace(assignedUserName))
            RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.Assignment, $"Se asignó el usuario \"{assignedUserName}\" a la solicitud.");
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Asignación duplicada o datos inválidos.");
        }
        return CreatedAtAction(nameof(GetAssignments), new { requestId }, row);
    }

    [HttpDelete("{assignmentId:int}")]
    public async Task<IActionResult> DeleteAssignment(int requestId, int assignmentId)
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

        var row = await _context.RequestAssignments
            .Include(a => a.Team)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.RequestAssignmentId == assignmentId && a.RequestId == requestId);
        if (row == null) return NotFound();

        var label = row.Team?.Name ?? row.User?.FullName ?? "asignación";
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.Assignment, $"Se quitó \"{label}\" de la solicitud.");
        _context.RequestAssignments.Remove(row);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
