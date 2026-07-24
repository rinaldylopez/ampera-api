namespace AuditWorkflow.Services;

public interface IFileStorageService
{
    string RootPath { get; }

    /// <summary>Clave relativa bajo la raíz (p. ej. requests/12/guid_file.pdf).</summary>
    string BuildKey(params string[] segments);

    /// <summary>Normaliza rutas legadas (/uploads/…) a clave de almacenamiento.</summary>
    string NormalizeStoredKey(string? storedPath);

    /// <summary>Guarda un archivo subido y devuelve la clave almacenada.</summary>
    Task<string> SaveUploadedFileAsync(IFormFile file, string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Resuelve ruta física (almacén privado o wwwroot legado).</summary>
    bool TryResolvePhysicalPath(string? storedPath, out string fullPath);

    void TryDeleteStoredFile(string? storedPath);

    string? GuessContentType(string? storedPath, string? downloadFileName = null);
}
