using AuditWorkflow.Settings;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Services;

public class FileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly string _legacyWebRoot;

    public FileStorageService(IWebHostEnvironment env, IOptions<FileStorageSettings> options)
    {
        _legacyWebRoot = !string.IsNullOrWhiteSpace(env.WebRootPath)
            ? env.WebRootPath
            : Path.Combine(env.ContentRootPath, "wwwroot");

        var configured = Environment.GetEnvironmentVariable("FILE_STORAGE_ROOT")?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
            configured = options.Value?.RootPath?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
        {
            var repoUploads = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "AmperaData", "uploads"));
            configured = repoUploads;
        }

        _rootPath = Path.GetFullPath(configured);
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public string BuildKey(params string[] segments)
    {
        if (segments == null || segments.Length == 0)
            throw new ArgumentException("At least one path segment is required.", nameof(segments));

        var parts = segments
            .SelectMany(s => (s ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p is not "." and not "..")
            .ToArray();

        if (parts.Length == 0)
            throw new ArgumentException("Invalid storage key segments.", nameof(segments));

        return string.Join('/', parts);
    }

    public string NormalizeStoredKey(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return "";

        var p = storedPath.Trim().Replace('\\', '/');
        while (p.StartsWith('/')) p = p[1..];

        if (p.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            p = p["uploads/".Length..];

        return p;
    }

    public async Task<string> SaveUploadedFileAsync(IFormFile file, string storageKey, CancellationToken cancellationToken = default)
    {
        var key = NormalizeStoredKey(storageKey);
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key is required.", nameof(storageKey));

        var fullPath = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = System.IO.File.Create(fullPath);
        await file.CopyToAsync(fs, cancellationToken);
        return key;
    }

    public bool TryResolvePhysicalPath(string storedPath, out string fullPath)
    {
        fullPath = "";
        var key = NormalizeStoredKey(storedPath);
        if (string.IsNullOrWhiteSpace(key)) return false;

        var privatePath = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(privatePath))
        {
            fullPath = privatePath;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_legacyWebRoot))
        {
            var legacyKey = key;
            if (!legacyKey.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
                legacyKey = $"uploads/{legacyKey}";

            var legacyPath = Path.Combine(
                _legacyWebRoot,
                legacyKey.Replace('/', Path.DirectorySeparatorChar));

            if (System.IO.File.Exists(legacyPath))
            {
                fullPath = legacyPath;
                return true;
            }
        }

        return false;
    }

    public void TryDeleteStoredFile(string storedPath)
    {
        if (!TryResolvePhysicalPath(storedPath, out var fullPath)) return;
        try
        {
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
        catch
        {
            /* ignorar IO */
        }
    }

    public string GuessContentType(string storedPath, string downloadFileName = null)
    {
        var name = !string.IsNullOrWhiteSpace(downloadFileName)
            ? downloadFileName
            : storedPath;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".json" => "application/json",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".zip" => "application/zip",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };
    }
}
