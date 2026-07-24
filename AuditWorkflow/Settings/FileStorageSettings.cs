namespace AuditWorkflow.Settings;

public class FileStorageSettings
{
    /// <summary>
    /// Raíz privada de archivos (p. ej. C:\AmperaData\uploads o …/AmperaData/uploads en desarrollo).
    /// Vacío: se resuelve desde FILE_STORAGE_ROOT o carpeta AmperaData del repositorio.
    /// </summary>
    public string RootPath { get; set; } = "";
}
