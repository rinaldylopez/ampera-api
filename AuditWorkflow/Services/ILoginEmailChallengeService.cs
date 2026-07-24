using AuditWorkflow.Data;
using AuditWorkflow.Models.Auth;

namespace AuditWorkflow.Services;

public interface ILoginEmailChallengeService
{
    /// <summary>Valida credenciales y envía código de 6 dígitos por correo. Null si credenciales incorrectas.</summary>
    Task<LoginRequestCodeResponse?> RequestCodeAsync(AuthRequest authRequest, AppDbContext context, CancellationToken cancellationToken = default);

    /// <summary>Verifica código y emite sesión. Null si código incorrecto, expirado o demasiados intentos.</summary>
    Task<UserResponse?> VerifyCodeAsync(LoginVerifyCodeRequest request, AppDbContext context, CancellationToken cancellationToken = default);
}
