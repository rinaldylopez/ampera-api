using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/comments")]
[ApiController]
public class RequestCommentsController(AppDbContext context, IFileStorageService fileStorage, IRequestCommentsRealtimeNotifier realtime) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IFileStorageService _fileStorage = fileStorage;
    private readonly IRequestCommentsRealtimeNotifier _realtime = realtime;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestComment>>> GetComments(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestComments
            .AsNoTracking()
            .Include(c => c.User)
            .Where(c => c.RequestId == requestId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<RequestComment>> PostComment(int requestId, [FromBody] RequestComment comment)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();
        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");
        if (!await TenantQueries.UserBelongsToFirmAsync(_context, userId.Value, firmId.Value))
            return BadRequest();

        comment.RequestId = requestId;
        comment.UserId = userId.Value;
        comment.CreatedAt = DateTime.UtcNow;
        comment.Request = null;
        comment.User = null;
        _context.RequestComments.Add(comment);
        var media = string.IsNullOrWhiteSpace(comment.AttachmentType)
            ? ""
            : comment.AttachmentType == "image"
                ? " con imagen adjunta"
                : comment.AttachmentType == "audio"
                    ? " con audio adjunto"
                    : " con adjunto";
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.Comment, $"Se envió un mensaje{media}.");
        await _context.SaveChangesAsync();

        var persisted = await _context.RequestComments
            .AsNoTracking()
            .Include(c => c.User)
            .FirstAsync(c => c.RequestCommentId == comment.RequestCommentId);
        await _realtime.NotifyCommentAdded(requestId, persisted);

        return CreatedAtAction(nameof(GetComments), new { requestId }, persisted);
    }

    [HttpGet("{commentId:int}/attachment")]
    public async Task<IActionResult> DownloadCommentAttachment(int requestId, int commentId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestComments
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RequestCommentId == commentId && c.RequestId == requestId);
        if (row == null || string.IsNullOrWhiteSpace(row.AttachmentUrl)) return NotFound();

        var downloadName = Path.GetFileName(_fileStorage.NormalizeStoredKey(row.AttachmentUrl));
        return StoredFileResult.TryOpen(_fileStorage, row.AttachmentUrl, downloadName, forceDownload: false) ?? NotFound();
    }

    [HttpPost("upload-attachment")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<object>> UploadCommentAttachment(int requestId, IFormFile file)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();
        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");
        if (file == null || file.Length == 0) return BadRequest("Archivo requerido.");

        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        var attachmentType = contentType.StartsWith("image/")
            ? "image"
            : contentType.StartsWith("audio/")
                ? "audio"
                : "";
        if (string.IsNullOrWhiteSpace(attachmentType))
            return BadRequest("Solo se permiten imágenes o audio.");

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var storageKey = _fileStorage.BuildKey("request-comments", requestId.ToString(), fileName);
        await _fileStorage.SaveUploadedFileAsync(file, storageKey);

        return Ok(new { attachmentUrl = storageKey, attachmentType });
    }

    [HttpDelete("{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int requestId, int commentId)
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

        var row = await _context.RequestComments
            .FirstOrDefaultAsync(c => c.RequestCommentId == commentId && c.RequestId == requestId);
        if (row == null) return NotFound();

        var removedId = row.RequestCommentId;
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.Comment, "Se eliminó un mensaje de la solicitud.");
        _context.RequestComments.Remove(row);
        await _context.SaveChangesAsync();
        await _realtime.NotifyCommentRemoved(requestId, removedId);
        return NoContent();
    }
}
