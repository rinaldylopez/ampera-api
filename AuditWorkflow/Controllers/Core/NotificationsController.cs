using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

public record UserNotificationDto(
    int UserNotificationId,
    string Title,
    string Body,
    string LinkPath,
    DateTime CreatedAt,
    DateTime? ReadAt,
    byte Kind,
    int? RequestId);

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class NotificationsController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserNotificationDto>>> GetMine([FromQuery] int take = 50)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        take = Math.Clamp(take, 1, 100);

        var rows = await _context.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId.Value && n.FirmId == firmId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new UserNotificationDto(
                n.UserNotificationId,
                n.Title,
                n.Body,
                n.LinkPath,
                n.CreatedAt,
                n.ReadAt,
                n.Kind,
                n.RequestId))
            .ToListAsync();

        return rows;
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var count = await _context.UserNotifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId.Value && n.FirmId == firmId.Value && n.ReadAt == null);

        return count;
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var row = await _context.UserNotifications.FirstOrDefaultAsync(
            n => n.UserNotificationId == id && n.UserId == userId.Value && n.FirmId == firmId.Value);
        if (row == null) return NotFound();
        if (row.ReadAt == null)
        {
            row.ReadAt = ServerClock.Now;
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var now = ServerClock.Now;
        await _context.UserNotifications
            .Where(n => n.UserId == userId.Value && n.FirmId == firmId.Value && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));

        return NoContent();
    }
}
