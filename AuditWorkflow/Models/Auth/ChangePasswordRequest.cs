namespace AuditWorkflow.Models.Auth;

/// <summary>Cambio de contraseña del usuario autenticado (valida la contraseña actual).</summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
