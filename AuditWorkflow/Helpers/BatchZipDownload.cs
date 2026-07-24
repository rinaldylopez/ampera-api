using System.IO.Compression;
using AuditWorkflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuditWorkflow.Helpers;

public static class BatchZipDownload
{
    public static async Task<IActionResult?> TryCreateZipAsync(
        IFileStorageService storage,
        IReadOnlyList<(string FilePath, string FileName)> files,
        string zipDownloadName)
    {
        if (files.Count == 0) return null;

        if (files.Count == 1)
            return StoredFileResult.TryOpen(storage, files[0].FilePath, files[0].FileName);

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (filePath, fileName) in files)
            {
                if (!storage.TryResolvePhysicalPath(filePath, out var fullPath)) continue;

                var entryName = UniqueEntryName(fileName, usedNames);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await fileStream.CopyToAsync(entryStream);
            }
        }

        if (ms.Length == 0) return null;

        ms.Position = 0;
        return new FileStreamResult(ms, "application/zip") { FileDownloadName = zipDownloadName };
    }

    private static string UniqueEntryName(string fileName, HashSet<string> used)
    {
        var baseName = string.IsNullOrWhiteSpace(fileName) ? "documento" : Path.GetFileName(fileName);
        if (used.Add(baseName)) return baseName;

        var ext = Path.GetExtension(baseName);
        var stem = Path.GetFileNameWithoutExtension(baseName);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (used.Add(candidate)) return candidate;
        }
    }
}
