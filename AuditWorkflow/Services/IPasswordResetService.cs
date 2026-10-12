using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;

namespace AuditWorkflow.Services;

public interface IPasswordResetService
{
    /// <summary>
    /// Invalida tokens previos, crea uno nuevo y envía el enlace por correo.
    /// </summary>
    Task SendResetLinkAsync(
        User targetUser,
        int? requestedByUserId,
        string firmDisplayName,
        AppDbContext context,
        CancellationToken cancellationToken = default);

    /// <summary>True si el token es válido (no usado, no expirado).</summary>
    Task<bool> IsTokenValidAsync(string rawToken, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canjea el token, establece la nueva contraseña, limpia MustChangePassword y revoca refresh tokens.
    /// </summary>
    Task<bool> CompleteResetAsync(
        string rawToken,
        string newPassword,
        AppDbContext context,
        CancellationToken cancellationToken = default);
}
