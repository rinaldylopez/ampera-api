namespace AuditWorkflow.Models.Global;

/// <summary>
/// Roles guardados en <see cref="User.SecurityRole"/> y emitidos como rol del JWT para la autorización (<c>User.IsInRole</c>).
/// Admin: registrar firmas y mantener catálogo global (países, módulos, perfiles de navegación).
/// Operator y Standard: uso diario del sistema según el perfil de UI; no tienen esos endpoints reservados a Admin.
/// </summary>
public static class UserSecurityRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Standard = "Standard";

    public static readonly IReadOnlyList<string> All = [Admin, Operator, Standard];

    public static bool IsValid(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var t = role.Trim();
        foreach (var x in All)
        {
            if (string.Equals(x, t, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
