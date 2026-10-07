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
[Route("api/requests/{requestId:int}/provided-files")]
[ApiController]
public class RequestProvidedFilesController(AppDbContext context, IFileStorageService fileStorage) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IFileStorageService _fileStorage = fileStorage;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestProvidedFile>>> GetProvidedFiles(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestProvidedFiles
            .AsNoTracking()
            .Include(a => a.UploadedBy)
            .Where(a => a.RequestId == requestId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();
    }

    [HttpGet("{providedFileId:int}")]
    public async Task<ActionResult<RequestProvidedFile>> GetProvidedFile(int requestId, int providedFileId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestProvidedFiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.UploadedBy)
            .Include(a => a.DeletedBy)
            .FirstOrDefaultAsync(a => a.RequestProvidedFileId == providedFileId && a.RequestId == requestId);

        return row == null ? NotFound() : row;
    }

    public class ProvidedFileBatchDownloadInput
    {
        public int[] ProvidedFileIds { get; set; } = [];
    }

    [HttpPost("download-batch")]
    public async Task<IActionResult> DownloadProvidedFilesBatch(int requestId, [FromBody] ProvidedFileBatchDownloadInput input)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var ids = (input.ProvidedFileIds ?? []).Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0) return BadRequest("Debe indicar al menos un archivo.");

        var rows = await _context.RequestProvidedFiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.RequestId == requestId && ids.Contains(a.RequestProvidedFileId))
            .ToListAsync();

        if (rows.Count == 0) return NotFound();

        var requestNumber = await _context.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => r.RequestNumber)
            .FirstOrDefaultAsync();
        var zipName = requestNumber > 0
            ? $"solicitud-{requestNumber}-documentos-compartidos.zip"
            : $"solicitud-{requestId}-documentos-compartidos.zip";

        var files = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.FilePath))
            .Select(r => (r.FilePath!, r.FileName ?? "documento"))
            .ToList();

        return await BatchZipDownload.TryCreateZipAsync(_fileStorage, files, zipName) ?? NotFound();
    }

    [HttpGet("{providedFileId:int}/download")]
    public async Task<IActionResult> DownloadProvidedFile(int requestId, int providedFileId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestProvidedFiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.RequestProvidedFileId == providedFileId && a.RequestId == requestId);
        if (row == null || string.IsNullOrWhiteSpace(row.FilePath)) return NotFound();

        return StoredFileResult.TryOpen(_fileStorage, row.FilePath, row.FileName) ?? NotFound();
    }

    [HttpGet("deleted")]
    public async Task<ActionResult<IEnumerable<RequestProvidedFile>>> GetDeletedProvidedFiles(int requestId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestProvidedFiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.UploadedBy)
            .Include(a => a.DeletedBy)
            .Where(a => a.RequestId == requestId && a.IsDeleted)
            .OrderByDescending(a => a.DeletedAt)
            .ToListAsync();
    }

    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<RequestProvidedFile>> UploadProvidedFile(int requestId, IFormFile file)
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
        if (file == null || file.Length == 0)
            return BadRequest("Debe enviar un archivo.");

        var displayName = SanitizeFileName(file.FileName ?? string.Empty);
        if (displayName.Length > 255)
            displayName = displayName[..255];

        var storedFileName = $"{Guid.NewGuid():N}_{SanitizeFileName(file.FileName ?? string.Empty)}";
        var storageKey = _fileStorage.BuildKey(
            "requests",
            requestId.ToString(CultureInfo.InvariantCulture),
            "provided",
            storedFileName);
        await _fileStorage.SaveUploadedFileAsync(file, storageKey);

        var entity = new RequestProvidedFile
        {
            RequestId = requestId,
            FileName = displayName,
            FilePath = storageKey,
            UploadedById = userId.Value,
            UploadedAt = ServerClock.Now
        };
        _context.RequestProvidedFiles.Add(entity);
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.FirmFile, $"La firma subió el archivo \"{displayName}\".");
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProvidedFile), new { requestId, providedFileId = entity.RequestProvidedFileId }, entity);
    }

    [HttpDelete("{providedFileId:int}")]
    public async Task<IActionResult> DeleteProvidedFile(int requestId, int providedFileId)
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

        var row = await _context.RequestProvidedFiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.RequestProvidedFileId == providedFileId && a.RequestId == requestId);
        if (row == null) return NotFound();

        row.IsDeleted = true;
        row.DeletedAt = ServerClock.Now;
        row.DeletedById = userId;
        RequestHistoryLog.Add(_context, requestId, userId.Value, RequestHistoryLog.FirmFile, $"La firma eliminó el archivo \"{row.FileName}\".");
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static string SanitizeFileName(string name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "documento" : Path.GetFileName(name.Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return string.IsNullOrEmpty(n) ? "documento" : n;
    }
}
