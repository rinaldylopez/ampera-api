using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/requests/{requestId:int}/attachments")]
[ApiController]
public class RequestAttachmentsController(AppDbContext context, IFileStorageService fileStorage) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IFileStorageService _fileStorage = fileStorage;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestAttachment>>> GetAttachments(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestAttachments
            .AsNoTracking()
            .Include(a => a.UploadedBy)
            .Where(a => a.RequestId == requestId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();
    }

    [HttpGet("{attachmentId:int}")]
    public async Task<ActionResult<RequestAttachment>> GetAttachment(int requestId, int attachmentId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestAttachments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.UploadedBy)
            .Include(a => a.DeletedBy)
            .FirstOrDefaultAsync(a => a.RequestAttachmentId == attachmentId && a.RequestId == requestId);

        return row == null ? NotFound() : row;
    }

    public class AttachmentBatchDownloadInput
    {
        public int[] AttachmentIds { get; set; } = [];
    }

    [HttpPost("download-batch")]
    public async Task<IActionResult> DownloadAttachmentsBatch(int requestId, [FromBody] AttachmentBatchDownloadInput input)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var ids = (input.AttachmentIds ?? []).Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0) return BadRequest("Debe indicar al menos un adjunto.");

        var rows = await _context.RequestAttachments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.RequestId == requestId && ids.Contains(a.RequestAttachmentId))
            .ToListAsync();

        if (rows.Count == 0) return NotFound();

        var requestNumber = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => r.RequestNumber)
            .FirstOrDefaultAsync();
        var zipName = requestNumber > 0
            ? $"solicitud-{requestNumber}-adjuntos.zip"
            : $"solicitud-{requestId}-adjuntos.zip";

        var files = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.FilePath))
            .Select(r => (r.FilePath!, r.FileName ?? "documento"))
            .ToList();

        return await BatchZipDownload.TryCreateZipAsync(_fileStorage, files, zipName) ?? NotFound();
    }

    [HttpGet("{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadAttachment(int requestId, int attachmentId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestAttachments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.RequestAttachmentId == attachmentId && a.RequestId == requestId);
        if (row == null || string.IsNullOrWhiteSpace(row.FilePath)) return NotFound();

        return StoredFileResult.TryOpen(_fileStorage, row.FilePath, row.FileName) ?? NotFound();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<IEnumerable<RequestAttachment>>> GetDeletedAttachments(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestAttachments
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.UploadedBy)
            .Include(a => a.DeletedBy)
            .Where(a => a.RequestId == requestId && a.IsDeleted)
            .OrderByDescending(a => a.DeletedAt)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<RequestAttachment>> PostAttachment(int requestId, [FromBody] RequestAttachment attachment)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();
        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return BadRequest("La documentación la aportan los usuarios cliente.");
        if (!await TenantQueries.UserBelongsToFirmAsync(_context, userId.Value, firmId.Value))
            return BadRequest();

        var requestState = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => new
            {
                r.PortalHandoffStatus,
                ProjectStatus = r.Project.Status
            })
            .FirstOrDefaultAsync();
        if (requestState == null) return NotFound();
        if (!string.Equals(requestState.ProjectStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo puede subir documentación si el proyecto está activo.");
        if (!TenantQueries.RequestAllowsClientDocumentUpload(requestState.PortalHandoffStatus))
            return BadRequest("Solo puede registrar adjuntos mientras la solicitud está con el cliente.");

        attachment.RequestId = requestId;
        attachment.UploadedById = userId.Value;
        attachment.UploadedAt = ServerClock.Now;
        attachment.FileStatus = AttachmentStatus.Pending;
        attachment.Request = null;
        attachment.UploadedBy = null;
        attachment.DeletedBy = null;
        _context.RequestAttachments.Add(attachment);
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.ClientFile, $"El cliente registró el archivo \"{attachment.FileName}\".");
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAttachment), new { requestId, attachmentId = attachment.RequestAttachmentId }, attachment);
    }

    /// <summary>Subida multipart para portal cliente (almacén privado AmperaData).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<RequestAttachment>> UploadAttachment(int requestId, IFormFile file)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();
        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return BadRequest("Solo los usuarios cliente pueden subir archivos.");
        if (!await TenantQueries.UserBelongsToFirmAsync(_context, userId.Value, firmId.Value))
            return BadRequest();
        if (file == null || file.Length == 0)
            return BadRequest("Debe enviar un archivo.");

        var requestState = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => new
            {
                r.PortalHandoffStatus,
                ProjectStatus = r.Project.Status
            })
            .FirstOrDefaultAsync();
        if (requestState == null) return NotFound();
        if (!string.Equals(requestState.ProjectStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo puede subir archivos si el proyecto está activo.");
        if (!TenantQueries.RequestAllowsClientDocumentUpload(requestState.PortalHandoffStatus))
            return BadRequest("Solo puede subir archivos mientras la solicitud está con el cliente.");

        var displayName = SanitizeFileName(file.FileName ?? string.Empty);
        if (displayName.Length > 255)
            displayName = displayName[..255];

        var storedFileName = $"{Guid.NewGuid():N}_{SanitizeFileName(file.FileName ?? string.Empty)}";
        var storageKey = _fileStorage.BuildKey(
            "requests",
            requestId.ToString(CultureInfo.InvariantCulture),
            storedFileName);
        await _fileStorage.SaveUploadedFileAsync(file, storageKey);

        var entity = new RequestAttachment
        {
            RequestId = requestId,
            FileName = displayName,
            FilePath = storageKey,
            UploadedById = userId.Value,
            UploadedAt = ServerClock.Now,
            FileStatus = AttachmentStatus.Pending,
            RejectionReason = null
        };
        _context.RequestAttachments.Add(entity);
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.ClientFile, $"El cliente subió el archivo \"{displayName}\".");
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAttachment), new { requestId, attachmentId = entity.RequestAttachmentId }, entity);
    }

    private static string SanitizeFileName(string name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "documento" : Path.GetFileName(name.Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return string.IsNullOrEmpty(n) ? "documento" : n;
    }

    public class AttachmentStatusInput
    {
        public AttachmentStatus FileStatus { get; set; }
        public string RejectionReason { get; set; } = null;
    }

    [HttpPut("{attachmentId:int}")]
    public async Task<IActionResult> PutAttachment(int requestId, int attachmentId, [FromBody] AttachmentStatusInput input)
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

        var handoff = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => r.PortalHandoffStatus)
            .FirstOrDefaultAsync();
        if (!TenantQueries.RequestAllowsFirmAttachmentReview(handoff))
            return BadRequest("Solo puede aceptar o rechazar adjuntos cuando la solicitud está en revisión por la firma.");

        var row = await _context.RequestAttachments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.RequestAttachmentId == attachmentId && a.RequestId == requestId);
        if (row == null) return NotFound();

        if (input.FileStatus == AttachmentStatus.Rejected && string.IsNullOrWhiteSpace(input.RejectionReason))
            return BadRequest("Debe indicar el motivo del rechazo.");

        row.FileStatus = input.FileStatus;
        row.RejectionReason = input.FileStatus == AttachmentStatus.Accepted
            ? null
            : string.IsNullOrWhiteSpace(input.RejectionReason) ? null : input.RejectionReason.Trim();
        var statusText = input.FileStatus == AttachmentStatus.Accepted
            ? "aceptó"
            : input.FileStatus == AttachmentStatus.Rejected
                ? "rechazó"
                : input.FileStatus == AttachmentStatus.Superseded
                    ? "marcó como reemplazado"
                    : "actualizó";
        var reason = string.IsNullOrWhiteSpace(row.RejectionReason) ? "" : $" Motivo: {row.RejectionReason}";
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.ClientFile, $"La firma {statusText} el archivo \"{row.FileName}\".{reason}");
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{attachmentId:int}")]
    public async Task<IActionResult> DeleteAttachment(int requestId, int attachmentId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        var isClientPortal = await TenantQueries.UserIsClientPortalAsync(_context, userId.Value);
        if (isClientPortal)
        {
            if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
                return NotFound();
        }
        else if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))
        {
            return NotFound();
        }

        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))
            return BadRequest("La solicitud está cerrada.");

        if (!isClientPortal)
            return Forbid();

        var handoff = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => r.PortalHandoffStatus)
            .FirstOrDefaultAsync();
        if (isClientPortal && !TenantQueries.RequestAllowsClientDocumentUpload(handoff))
            return BadRequest("Solo puede eliminar adjuntos mientras la solicitud está con el cliente.");

        var row = await _context.RequestAttachments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.RequestAttachmentId == attachmentId && a.RequestId == requestId);
        if (row == null) return NotFound();
        if (row.FileStatus is AttachmentStatus.Accepted or AttachmentStatus.Rejected)
            return BadRequest("No puede eliminar un archivo que la firma ya aceptó o rechazó.");

        row.IsDeleted = true;
        row.DeletedAt = ServerClock.Now;
        row.DeletedById = userId;
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.ClientFile, $"El cliente eliminó el archivo \"{row.FileName}\".");
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
