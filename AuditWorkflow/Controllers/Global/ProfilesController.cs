using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ProfilesController(AppDbContext context, IProfileResourcePermissionService permissionService) : ControllerBase
{
    private const int ClientPortalProfileId = ClientPortalProfileHelper.ExpectedProfileId;
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _permissionService = permissionService;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Profile>>> GetProfiles()
    {
        return await _context.Profiles
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Profile>> GetProfile(int id)
    {
        var profile = await _context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.ProfileId == id);
        return profile == null ? NotFound() : profile;
    }

    [HttpPost]
    public async Task<ActionResult<Profile>> PostProfile([FromBody] ProfileInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest("El nombre es obligatorio.");

        var entity = new Profile { Name = input.Name.Trim() };
        _context.Profiles.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProfile), new { id = entity.ProfileId }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutProfile(int id, [FromBody] ProfileInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != input.ProfileId)
            return BadRequest();
        if (id == ClientPortalProfileId)
            return Conflict("El perfil del portal cliente es interno y no se puede modificar.");
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest("El nombre es obligatorio.");

        var entity = await _context.Profiles.FindAsync(id);
        if (entity == null)
            return NotFound();

        entity.Name = input.Name.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Profiles.AnyAsync(e => e.ProfileId == id))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProfile(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null)
            return NotFound();
        if (id == ClientPortalProfileId)
            return Conflict("El perfil del portal cliente es interno y no se puede eliminar.");

        var hasUsers = await _context.Users.AnyAsync(u => u.ProfileId == id);
        if (hasUsers)
            return Conflict("Hay usuarios asignados a este perfil; no se puede eliminar.");

        _context.Profiles.Remove(profile);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ----- Ítems de menú asignados al perfil (ProfileItem) -----

    [HttpGet("{profileId:int}/profile-items")]
    public async Task<ActionResult<IEnumerable<ProfileItemRowDto>>> GetProfileItems(int profileId)
    {
        if (!await _context.Profiles.AnyAsync(p => p.ProfileId == profileId))
            return NotFound();

        return await _context.ProfileItems
            .AsNoTracking()
            .Where(pi => pi.ProfileId == profileId)
            .Select(pi => new ProfileItemRowDto
            {
                ProfileItemId = pi.ProfileItemId,
                ProfileId = pi.ProfileId,
                NavigationItemId = pi.NavigationItemId,
                NavigationName = pi.NavigationItem.Name,
                Path = pi.NavigationItem.Path,
                ModuleName = pi.NavigationItem.Module.Name
            })
            .OrderBy(d => d.ModuleName)
            .ThenBy(d => d.NavigationName)
            .ToListAsync();
    }

    [HttpPost("{profileId:int}/profile-items")]
    public async Task<ActionResult<ProfileItem>> PostProfileItem(int profileId, [FromBody] ProfileItemLinkInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (profileId == ClientPortalProfileId)
            return Conflict("El perfil del portal cliente no usa ítems de navegación configurables.");
        if (!await _context.Profiles.AnyAsync(p => p.ProfileId == profileId))
            return NotFound();
        if (input.NavigationItemId <= 0)
            return BadRequest("NavigationItemId no válido.");

        if (!await _context.NavigationItems.AnyAsync(n => n.NavigationItemId == input.NavigationItemId))
            return BadRequest("El ítem de navegación no existe.");

        if (await _context.ProfileItems.AnyAsync(pi =>
                pi.ProfileId == profileId && pi.NavigationItemId == input.NavigationItemId))
            return Conflict("El ítem ya está asignado a este perfil.");

        var entity = new ProfileItem
        {
            ProfileId = profileId,
            NavigationItemId = input.NavigationItemId
        };
        _context.ProfileItems.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProfileItems), new { profileId }, entity);
    }

    [HttpDelete("{profileId:int}/profile-items/{profileItemId:int}")]
    public async Task<IActionResult> DeleteProfileItem(int profileId, int profileItemId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (profileId == ClientPortalProfileId)
            return Conflict("El perfil del portal cliente no usa ítems de navegación configurables.");

        var entity = await _context.ProfileItems
            .FirstOrDefaultAsync(pi => pi.ProfileItemId == profileItemId && pi.ProfileId == profileId);
        if (entity == null)
            return NotFound();

        _context.ProfileItems.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Reemplaza los ítems de menú del perfil por el conjunto enviado (lista vacía = quitar todos).
    /// </summary>
    [HttpPut("{profileId:int}/profile-items/sync")]
    public async Task<IActionResult> SyncProfileItems(int profileId, [FromBody] ProfileItemsSyncInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (profileId == ClientPortalProfileId)
            return Conflict("El perfil del portal cliente no usa ítems de navegación configurables.");
        if (!await _context.Profiles.AnyAsync(p => p.ProfileId == profileId))
            return NotFound();

        var wanted = (input.NavigationItemIds ?? []).Distinct().Where(id => id > 0).ToList();

        if (wanted.Count > 0)
        {
            var validCount = await _context.NavigationItems.CountAsync(n => wanted.Contains(n.NavigationItemId));
            if (validCount != wanted.Count)
                return BadRequest("Uno o más ítems de navegación no existen.");
        }

        var current = await _context.ProfileItems.Where(pi => pi.ProfileId == profileId).ToListAsync();
        var wantedSet = wanted.ToHashSet();

        foreach (var pi in current.Where(c => !wantedSet.Contains(c.NavigationItemId)))
            _context.ProfileItems.Remove(pi);

        var currentNavIds = current.Select(c => c.NavigationItemId).ToHashSet();
        foreach (var navId in wanted.Where(id => !currentNavIds.Contains(id)))
        {
            _context.ProfileItems.Add(new ProfileItem
            {
                ProfileId = profileId,
                NavigationItemId = navId
            });
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{profileId:int}/resource-permissions")]
    public async Task<ActionResult<ProfileResourcePermissionsDto>> GetResourcePermissions(int profileId)
    {
        if (!User.IsInRole(UserSecurityRoles.Admin))
            return Forbid();
        if (!await _context.Profiles.AsNoTracking().AnyAsync(p => p.ProfileId == profileId))
            return NotFound();

        var dto = await _permissionService.GetMatrixForProfileEditorAsync(profileId);
        return Ok(dto);
    }

    /// <summary>
    /// Persiste los cuatro recursos con sus flags C/U/D. Reemplaza filas previas del perfil.
    /// </summary>
    [HttpPut("{profileId:int}/resource-permissions")]
    public async Task<IActionResult> PutResourcePermissions(int profileId,
        [FromBody] ProfileResourcePermissionsPutInput input)
    {
        if (!User.IsInRole(UserSecurityRoles.Admin))
            return Forbid();
        if (!await _context.Profiles.AnyAsync(p => p.ProfileId == profileId))
            return NotFound();
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var items = input.Items ?? [];
        if (items.Count != 4)
            return BadRequest("Debe enviar exactamente cuatro filas: teams, customers, projects, requests.");

        var seen = new HashSet<ProfileProtectedResource>();
        var toInsert = new List<ProfileResourcePermission>();
        foreach (var row in items)
        {
            if (!ProfileResourcePermissionService.TryParseResourceKey(row.Resource, out var r))
                return BadRequest($"Recurso no válido: {row.Resource}");
            if (!seen.Add(r))
                return BadRequest("Hay recursos duplicados en el cuerpo.");
            toInsert.Add(new ProfileResourcePermission
            {
                ProfileId = profileId,
                Resource = r,
                CanCreate = row.Create,
                CanUpdate = row.Update,
                CanDelete = row.Delete
            });
        }

        if (seen.Count != Enum.GetValues<ProfileProtectedResource>().Length)
            return BadRequest("Faltan recursos en el cuerpo.");

        var existing = await _context.ProfileResourcePermissions.Where(p => p.ProfileId == profileId).ToListAsync();
        _context.ProfileResourcePermissions.RemoveRange(existing);
        _context.ProfileResourcePermissions.AddRange(toInsert);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
