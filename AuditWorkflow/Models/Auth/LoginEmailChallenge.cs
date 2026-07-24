using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Auth;

/// <summary>
/// Código de un solo uso (6 dígitos por correo) entre contraseña válida y emisión de JWT.
/// </summary>
public class LoginEmailChallenge
{
    public int LoginEmailChallengeId { get; set; }

    public Guid ChallengeToken { get; set; }

    public int UserId { get; set; }

    /// <summary>SHA-256 hex del código + token + secreto de aplicación.</summary>
    [Required]
    [MaxLength(64)]
    public string CodeHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int FailedAttempts { get; set; }
}
