using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Auth;

public class LoginVerifyCodeRequest
{
    [Required]
    public string ChallengeToken { get; set; }

    [Required]
    public string Code { get; set; }

    /// <summary>Si se omite, la sesión usa la firma hogar del usuario.</summary>
    public int? LoginFirmId { get; set; }

    /// <summary>Solo portal: si se omite, se elige el cliente predeterminado o el primero accesible.</summary>
    public int? LoginCustomerId { get; set; }
}
