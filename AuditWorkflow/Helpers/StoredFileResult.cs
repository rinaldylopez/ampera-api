using AuditWorkflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuditWorkflow.Helpers;

public static class StoredFileResult
{
    public static IActionResult? TryOpen(
        IFileStorageService storage,
        string? storedPath,
        string? downloadFileName,
        bool forceDownload = true)
    {
        if (!storage.TryResolvePhysicalPath(storedPath, out var fullPath))
            return null;

        var contentType = storage.GuessContentType(storedPath, downloadFileName) ?? "application/octet-stream";
        var result = new PhysicalFileResult(fullPath, contentType)
        {
            EnableRangeProcessing = true
        };
        if (forceDownload)
            result.FileDownloadName = string.IsNullOrWhiteSpace(downloadFileName) ? Path.GetFileName(fullPath) : downloadFileName;
        return result;
    }
}
