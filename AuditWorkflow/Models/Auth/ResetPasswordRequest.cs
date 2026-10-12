using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Auth;

public class ResetPasswordRequest
{
    [Required]
    [MaxLength(200)]
    public string Token { get; set; } = "";

    [Required]
    [MinLength(6)]
    [MaxLength(200)]
    public string NewPassword { get; set; } = "";
}
