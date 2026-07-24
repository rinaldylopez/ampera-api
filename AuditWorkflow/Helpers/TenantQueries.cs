using AuditWorkflow.Data;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Helpers;

public static class TenantQueries
{
    /// <summary>Usuario interno de la firma (no portal cliente).</summary>
    public const int UserTypeFirm = 1;
    /// <summary>Usuario del cliente (portal / entrega documentación).</summary>
    public const int UserTypeClient = 2;

    public static Task<bool> UserIsClientPortalAsync(AppDbContext db, int userId) =>
        db.Users.AnyAsync(u => u.UserId == userId && u.UserType == UserTypeClient);

    /// <summary>Usuarios listables o editables en la firma activa (clientes por <see cref="User.FirmId"/>; internos por <see cref="UserFirmMembership"/>).</summary>
    public static IQueryable<User> UsersVisibleInActiveFirm(AppDbContext db, int activeFirmId) =>
        db.Users.Where(u =>
            (u.UserType == UserTypeClient && u.FirmId == activeFirmId)
            || (u.UserType == UserTypeFirm
                && db.UserFirmMemberships.Any(m => m.UserId == u.UserId && m.FirmId == activeFirmId)));

    /// <summary>
    /// Usuario puede operar en el contexto de la firma indicada (JWT <c>FirmId</c> / sesión activa).
    /// </summary>
    public static async Task<bool> UserBelongsToFirmAsync(AppDbContext db, int userId, int firmId)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (u == null) return false;
        if (u.UserType == UserTypeClient)
            return u.FirmId == firmId;
        return await db.UserFirmMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.FirmId == firmId);
    }

    /// <summary>Usuarios internos activos con membresía en la firma indicada (no portal cliente).</summary>
    public static IQueryable<User> ActiveFirmStaffInFirm(AppDbContext db, int firmId) =>
        db.Users.Where(u =>
            u.UserType == UserTypeFirm &&
            u.IsActive &&
            db.UserFirmMemberships.Any(m => m.UserId == u.UserId && m.FirmId == firmId));

    public static Task<User?> FindActiveFirmStaffByEmailInFirmAsync(
        AppDbContext db,
        int firmId,
        string emailNormalized,
        CancellationToken cancellationToken = default) =>
        ActiveFirmStaffInFirm(db, firmId)
            .Where(u => u.Email != null && u.Email.Trim().ToLower() == emailNormalized)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Usuario interno de firma activo con acceso a la firma indicada (no portal cliente).</summary>
    public static Task<bool> UserIsActiveFirmStaffInFirmAsync(AppDbContext db, int userId, int firmId) =>
        ActiveFirmStaffInFirm(db, firmId).AnyAsync(u => u.UserId == userId);

    /// <summary>
    /// Firma: solicitudes del tenant activo en JWT. Cliente: solo solicitudes de proyectos de su empresa (CustomerId).
    /// </summary>
    /// <param name="activeFirmId">Firma activa de la sesión (claim JWT).</param>
    /// <param name="activeCustomerIdFromJwt">Portal cliente: cliente activo (claim JWT). Ignorado para usuarios firma.</param>
    public static async Task<bool> RequestAccessibleToUserAsync(AppDbContext db, int requestId, int userId, int activeFirmId, int? activeCustomerIdFromJwt)
    {
        if (!await UserBelongsToFirmAsync(db, userId, activeFirmId))
            return false;

        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (u == null) return false;

        var inTenant = await db.Requests.AnyAsync(r => r.RequestId == requestId && r.Project.FirmId == activeFirmId);
        if (!inTenant) return false;
        if (u.UserType == UserTypeFirm) return true;
        // Portal: mismo vínculo al cliente que en listados (Customers.CustomerId vía JWT), no sólo tabla de membresías.
        if (u.UserType == UserTypeClient && activeCustomerIdFromJwt is int cid)
        {
            if (!u.IsActive) return false;
            var tiedToCustomer =
                u.CustomerId == cid
                || await db.UserCustomerMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId && m.CustomerId == cid);
            return tiedToCustomer && await db.Requests.AnyAsync(r => r.RequestId == requestId && r.Project.CustomerId == cid);
        }

        return false;
    }

    public static Task<bool> ProjectBelongsToFirmAsync(AppDbContext db, int projectId, int firmId) =>
        db.Projects.AnyAsync(p => p.ProjectId == projectId && p.FirmId == firmId);

    /// <param name="activeFirmId">Firma activa de la sesión (claim JWT).</param>
    /// <param name="activeCustomerIdFromJwt">Portal cliente: cliente activo (claim JWT).</param>
    public static async Task<bool> ProjectAccessibleToUserAsync(AppDbContext db, int projectId, int userId, int activeFirmId, int? activeCustomerIdFromJwt)
    {
        if (!await UserBelongsToFirmAsync(db, userId, activeFirmId))
            return false;

        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        if (u == null) return false;

        if (!await ProjectBelongsToFirmAsync(db, projectId, activeFirmId)) return false;
        if (u.UserType == UserTypeFirm) return true;
        if (u.UserType == UserTypeClient && activeCustomerIdFromJwt is int cid)
        {
            if (!u.IsActive) return false;
            var tiedToCustomer =
                u.CustomerId == cid
                || await db.UserCustomerMemberships.AsNoTracking()
                    .AnyAsync(m => m.UserId == userId && m.CustomerId == cid);
            return tiedToCustomer && await db.Projects.AnyAsync(p => p.ProjectId == projectId && p.CustomerId == cid);
        }

        return false;
    }

    public static Task<bool> TeamBelongsToFirmAsync(AppDbContext db, int teamId, int firmId) =>
        db.Teams.AnyAsync(t => t.TeamId == teamId && t.FirmId == firmId);

    public static Task<bool> CustomerBelongsToFirmAsync(AppDbContext db, int customerId, int firmId) =>
        db.Customers.AnyAsync(c => c.CustomerId == customerId && c.FirmId == firmId);

    /// <summary>
    /// Usuarios del portal cliente (<see cref="UserTypeClient"/>) activos para un cliente de la firma.
    /// Requerido para publicar solicitudes en el portal (no hay destinatarios si la lista está vacía).
    /// </summary>
    public static Task<bool> HasActiveClientPortalUsersAsync(
        AppDbContext db,
        int firmId,
        int customerId,
        CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().AnyAsync(
            u => u.FirmId == firmId
                 && u.UserType == UserTypeClient
                 && u.IsActive
                 && (u.CustomerId == customerId
                     || db.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)),
            cancellationToken);

    public static Task<bool> UserBelongsToCustomerAsync(
        AppDbContext db,
        int userId,
        int firmId,
        int customerId,
        CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().AnyAsync(
            u => u.UserId == userId
                 && u.FirmId == firmId
                 && u.UserType == UserTypeClient
                 && u.IsActive
                 && (u.CustomerId == customerId
                     || db.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)),
            cancellationToken);

    public static Task<bool> RequestBelongsToFirmAsync(AppDbContext db, int requestId, int firmId) =>
        db.Requests.AnyAsync(r => r.RequestId == requestId && r.Project.FirmId == firmId);

    /// <summary>
    /// Client-portal users tied to a customer via home <see cref="User.CustomerId"/> or <see cref="UserCustomerMembership"/>.
    /// </summary>
    public static async Task<HashSet<int>> GetClientUserAccessibleCustomerIdsAsync(
        AppDbContext db,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var u = await db.Users.AsNoTracking()
            .Where(x => x.UserId == userId && x.UserType == UserTypeClient)
            .Select(x => new { x.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (u == null) return [];

        var ids = await db.UserCustomerMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.CustomerId)
            .ToListAsync(cancellationToken);

        var set = ids.ToHashSet();
        if (u.CustomerId is int home)
            set.Add(home);
        return set;
    }

    /// <summary>
    /// Removes client users from customer-scoped teams when they no longer have access to that customer.
    /// Does not affect firm-wide teams or memberships for customers they can still access.
    /// </summary>
    public static async Task PruneIneligibleClientTeamMembershipsAsync(
        AppDbContext db,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var accessible = await GetClientUserAccessibleCustomerIdsAsync(db, userId, cancellationToken);
        if (accessible.Count == 0) return;

        var stale = await db.TeamMembers
            .Where(tm =>
                tm.UserId == userId
                && tm.Team.CustomerId != null
                && !accessible.Contains(tm.Team.CustomerId.Value))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return;

        db.TeamMembers.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Firm-wide team (<c>Team.CustomerId</c> null): only internal firm users (<see cref="UserTypeFirm"/>).
    /// Customer-scoped team: only client-portal users (<see cref="UserTypeClient"/>) tied to that customer.
    /// </summary>
    public static async Task<bool> UserEligibleForTeamMembershipAsync(AppDbContext db, int teamId, int userId, int firmId)
    {
        var team = await db.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TeamId == teamId && t.FirmId == firmId);
        if (team == null) return false;

        if (team.CustomerId is int customerId)
            return await db.Users.AsNoTracking().AnyAsync(
                u =>
                    u.UserId == userId &&
                    u.FirmId == firmId &&
                    u.UserType == UserTypeClient &&
                    u.IsActive &&
                    (u.CustomerId == customerId ||
                     db.UserCustomerMemberships.Any(m => m.UserId == userId && m.CustomerId == customerId)));

        if (!await UserBelongsToFirmAsync(db, userId, firmId)) return false;
        return await db.Users.AnyAsync(u => u.UserId == userId && u.UserType == UserTypeFirm);
    }

    /// <summary>
    /// Firm-wide teams apply to any project; customer-scoped teams only to projects of that customer.
    /// </summary>
    public static async Task<bool> TeamEligibleForProjectAsync(
        AppDbContext db,
        int teamId,
        int projectId,
        int firmId)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.FirmId == firmId);
        if (project == null) return false;

        var team = await db.Teams.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TeamId == teamId && t.FirmId == firmId);
        if (team == null) return false;

        if (team.CustomerId is int teamCustomerId)
            return teamCustomerId == project.CustomerId;

        return true;
    }

    /// <summary>
    /// Project staff: internal firm users or client-portal users tied to the project's customer.
    /// </summary>
    public static async Task<bool> UserEligibleForProjectStaffAsync(
        AppDbContext db,
        int userId,
        int projectId,
        int firmId)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.FirmId == firmId);
        if (project == null) return false;

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.FirmId == firmId && u.IsActive);
        if (user == null) return false;

        if (user.UserType == UserTypeFirm)
            return await UserBelongsToFirmAsync(db, userId, firmId);

        if (user.UserType == UserTypeClient)
        {
            return await db.Users.AsNoTracking().AnyAsync(
                u =>
                    u.UserId == userId &&
                    u.FirmId == firmId &&
                    u.IsActive &&
                    (u.CustomerId == project.CustomerId ||
                     db.UserCustomerMemberships.Any(m =>
                         m.UserId == userId && m.CustomerId == project.CustomerId)));
        }

        return false;
    }

    public static bool RequestAllowsClientDocumentUpload(int portalHandoffStatus) =>
        portalHandoffStatus == (int)RequestPortalHandoff.WithClient
        || portalHandoffStatus == (int)RequestPortalHandoff.ReturnedToClient;

    public static bool RequestAllowsFirmAttachmentReview(int portalHandoffStatus) =>
        portalHandoffStatus == (int)RequestPortalHandoff.WithFirm;

    public static Task<bool> RequestIsClosedAsync(AppDbContext db, int requestId) =>
        db.Requests.AsNoTracking().AnyAsync(r => r.RequestId == requestId && r.ClosedAt != null);

    /// <summary>True if the user is referenced in a way that blocks delete.</summary>
    public static async Task<bool> UserHasBlockingReferencesAsync(AppDbContext db, int userId)
    {
        if (await db.Projects.AnyAsync(p => p.ManagerId == userId || p.PartnerInChargeId == userId)) return true;
        if (await db.TeamMembers.AnyAsync(m => m.UserId == userId)) return true;
        if (await db.ProjectStaffs.AnyAsync(s => s.UserId == userId)) return true;
        if (await db.Requests.AnyAsync(r => r.FirmContactId == userId || r.UserCreatedId == userId || r.ClosedById == userId))
            return true;
        if (await db.RequestAssignments.AnyAsync(a => a.UserId == userId)) return true;
        if (await db.RequestComments.AnyAsync(c => c.UserId == userId)) return true;
        if (await db.RequestHistory.AnyAsync(h => h.ActionById == userId)) return true;
        if (await db.RequestAttachments.AnyAsync(a => a.UploadedById == userId || a.DeletedById == userId))
            return true;
        if (await db.RequestProvidedFiles.AnyAsync(a => a.UploadedById == userId || a.DeletedById == userId))
            return true;
        return false;
    }
}
