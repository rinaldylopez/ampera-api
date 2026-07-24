namespace AuditWorkflow.Models.Auth;

public class LoginRequestCodeResponse
{
    public string ChallengeToken { get; set; }

    public int ExpiresInSeconds { get; set; }
}
