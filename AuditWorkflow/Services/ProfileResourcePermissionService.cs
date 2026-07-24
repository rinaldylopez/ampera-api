using System.Security.Claims;
using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Services;

public sealed class ProfileResourcePermissionService(AppDbContext context) : IProfileResourcePermissionService
{
    private readonly AppDbContext _context = context;

    public async Task<bool> AllowsFirmMutationAsync(
        ClaimsPrincipal user,
        ProfileProtectedResource resource,
        ResourcePermissionAction action,
        CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        if (userId is null) return false;

        if (await _context.Users.AsNoTracking().AnyAsync(
                u => u.UserId == userId.Value && u.UserType == TenantQueries.UserTypeClient,
                cancellationToken))
            return true;

        if (user.IsInRole(UserSecurityRoles.Admin))
            return true;

        var profileId = await ResolveProfileIdAsync(user, userId.Value, cancellationToken);
        if (profileId is null) return false;

        var rows = await _context.ProfileResourcePermissions.AsNoTracking()
            .Where(p => p.ProfileId == profileId.Value)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return true;

        var row = rows.FirstOrDefault(r => r.Resource == resource);
        if (row is null)
            return false;

        return action switch
        {
            ResourcePermissionAction.Create => row.CanCreate,
            ResourcePermissionAction.Update => row.CanUpdate,
            ResourcePermissionAction.Delete => row.CanDelete,
            _ => false
        };
    }

    public async Task<ProfileResourcePermissionsDto> ResolveDtoForLoginAsync(int profileId, bool isSecurityAdmin,
        CancellationToken cancellationToken = default)
    {
        if (isSecurityAdmin)
            return ProfileResourcePermissionsDto.FullAccess;

        return await GetMatrixForProfileEditorAsync(profileId, cancellationToken);
    }

    public async Task<ProfileResourcePermissionsDto> GetMatrixForProfileEditorAsync(int profileId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.ProfileResourcePermissions.AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return ProfileResourcePermissionsDto.FullAccess;

        ProfileResourcePermissionsDto dto = new();
        foreach (ProfileProtectedResource r in Enum.GetValues<ProfileProtectedResource>())
        {
            var row = rows.FirstOrDefault(x => x.Resource == r);
            var triplet =
                row is null ? new ResourcePermissionTripletDto() :
                new ResourcePermissionTripletDto
                {
                    Create = row.CanCreate,
                    Update = row.CanUpdate,
                    Delete = row.CanDelete
                };
            Assign(dto, r, triplet);
        }

        return dto;
    }

    private async Task<int?> ResolveProfileIdAsync(ClaimsPrincipal user, int userId,
        CancellationToken cancellationToken)
    {
        var fromClaim = user.GetProfileId();
        if (fromClaim is > 0) return fromClaim;

        return await _context.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => (int?)u.ProfileId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void Assign(ProfileResourcePermissionsDto dto, ProfileProtectedResource resource,
        ResourcePermissionTripletDto triplet)
    {
        switch (resource)
        {
            case ProfileProtectedResource.Teams:
                dto.Teams = triplet;
                break;
            case ProfileProtectedResource.Customers:
                dto.Customers = triplet;
                break;
            case ProfileProtectedResource.Projects:
                dto.Projects = triplet;
                break;
            case ProfileProtectedResource.Requests:
                dto.Requests = triplet;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
        }
    }

    public static bool TryParseResourceKey(string raw, out ProfileProtectedResource resource)
    {
        resource = ProfileProtectedResource.Teams;
        var key = raw?.Trim().ToLowerInvariant() ?? "";
        return key switch
        {
            "teams" => As(ProfileProtectedResource.Teams, out resource),
            "customers" => As(ProfileProtectedResource.Customers, out resource),
            "projects" => As(ProfileProtectedResource.Projects, out resource),
            "requests" => As(ProfileProtectedResource.Requests, out resource),
            _ => false
        };

        static bool As(ProfileProtectedResource r, out ProfileProtectedResource o)
        {
            o = r;
            return true;
        }
    }
}
