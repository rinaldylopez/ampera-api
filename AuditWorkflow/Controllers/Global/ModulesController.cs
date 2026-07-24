using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ModulesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Module>>> GetModules()
    {
        return await _context.Modules
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Module>> GetModule(int id)
    {
        var module = await _context.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.ModuleId == id);
        return module == null ? NotFound() : module;
    }

    [HttpPost]
    public async Task<ActionResult<Module>> PostModule([FromBody] ModuleInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest("El nombre es obligatorio.");

        var entity = new Module
        {
            Name = input.Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(input.Icon) ? "" : input.Icon.Trim()
        };
        _context.Modules.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetModule), new { id = entity.ModuleId }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutModule(int id, [FromBody] ModuleInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != input.ModuleId)
            return BadRequest();
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest("El nombre es obligatorio.");

        var entity = await _context.Modules.FindAsync(id);
        if (entity == null)
            return NotFound();

        entity.Name = input.Name.Trim();
        entity.Icon = string.IsNullOrWhiteSpace(input.Icon) ? "" : input.Icon.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Modules.AnyAsync(e => e.ModuleId == id))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteModule(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var module = await _context.Modules.FindAsync(id);
        if (module == null)
            return NotFound();

        var hasItems = await _context.NavigationItems.AnyAsync(n => n.ModuleId == id);
        if (hasItems)
            return Conflict("El módulo tiene elementos de navegación asociados; no se puede eliminar.");

        _context.Modules.Remove(module);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ----- Ítems de navegación por módulo -----

    [HttpGet("{moduleId:int}/navigation-items")]
    public async Task<ActionResult<IEnumerable<NavigationItem>>> GetNavigationItems(int moduleId)
    {
        if (!await _context.Modules.AnyAsync(m => m.ModuleId == moduleId))
            return NotFound();

        return await _context.NavigationItems
            .AsNoTracking()
            .Where(n => n.ModuleId == moduleId)
            .OrderBy(n => n.Name)
            .ToListAsync();
    }

    [HttpPost("{moduleId:int}/navigation-items")]
    public async Task<ActionResult<NavigationItem>> PostNavigationItem(int moduleId, [FromBody] NavigationItemInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (!await _context.Modules.AnyAsync(m => m.ModuleId == moduleId))
            return NotFound();
        if (input.ModuleId != 0 && input.ModuleId != moduleId)
            return BadRequest();
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Path))
            return BadRequest("Nombre y ruta son obligatorios.");

        var entity = new NavigationItem
        {
            ModuleId = moduleId,
            Name = input.Name.Trim(),
            Path = input.Path.Trim()
        };
        _context.NavigationItems.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetNavigationItems), new { moduleId }, entity);
    }

    [HttpPut("{moduleId:int}/navigation-items/{itemId:int}")]
    public async Task<IActionResult> PutNavigationItem(int moduleId, int itemId, [FromBody] NavigationItemInput input)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (itemId != input.NavigationItemId || input.ModuleId != moduleId)
            return BadRequest();
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Path))
            return BadRequest("Nombre y ruta son obligatorios.");

        var entity = await _context.NavigationItems.FirstOrDefaultAsync(n => n.NavigationItemId == itemId && n.ModuleId == moduleId);
        if (entity == null)
            return NotFound();

        entity.Name = input.Name.Trim();
        entity.Path = input.Path.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.NavigationItems.AnyAsync(n => n.NavigationItemId == itemId))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{moduleId:int}/navigation-items/{itemId:int}")]
    public async Task<IActionResult> DeleteNavigationItem(int moduleId, int itemId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var entity = await _context.NavigationItems.FirstOrDefaultAsync(n => n.NavigationItemId == itemId && n.ModuleId == moduleId);
        if (entity == null)
            return NotFound();

        var inProfiles = await _context.ProfileItems.AnyAsync(p => p.NavigationItemId == itemId);
        if (inProfiles)
            return Conflict("El ítem está asignado a uno o más perfiles; no se puede eliminar.");

        _context.NavigationItems.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
