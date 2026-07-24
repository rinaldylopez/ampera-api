using System.Security.Claims;

namespace AuditWorkflow.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(id, out var v) ? v : null;
    }

    public static int? GetFirmId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst("FirmId")?.Value;
        return int.TryParse(id, out var v) ? v : null;
    }

    /// <summary>Cliente activo de sesión (portal); solo usuarios <c>UserType</c> cliente.</summary>
    public static int? GetCustomerId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst("CustomerId")?.Value;
        return int.TryParse(id, out var v) ? v : null;
    }

    public static int? GetProfileId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst("ProfileId")?.Value;
        return int.TryParse(id, out var v) ? v : null;
    }
}
