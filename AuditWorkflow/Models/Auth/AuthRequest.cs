using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Auth;

/// <summary>Credenciales de login. La contraseña viaja en claro sobre HTTPS y se compara tras SHA256 en servidor.</summary>
public class AuthRequest
{
    public string Email { get; set; }

    /// <summary>Contraseña en texto plano (nombre JSON recomendado: password).</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; }

    /// <summary>Compatibilidad con clientes que aún envían passwordHash (también texto plano, no es el hash almacenado).</summary>
    [JsonPropertyName("passwordHash")]
    public string PasswordLegacy { get; set; }

    /// <summary>Contraseña en claro a validar (prioriza <see cref="Password"/>).</summary>
    public string ResolvePlainPassword() =>
        !string.IsNullOrWhiteSpace(Password) ? Password : PasswordLegacy;
}
