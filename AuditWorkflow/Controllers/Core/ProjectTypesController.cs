using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ProjectTypesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    private static string NormCode(string code) => (code ?? "").Trim();
    private static string NormName(string name) => (name ?? "").Trim();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectType>>> GetProjectTypes()
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        return await _context.ProjectTypes.AsNoTracking()
            .Where(t => t.FirmId == firmId.Value)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectType>> GetProjectType(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var row = await _context.ProjectTypes.AsNoTracking().FirstOrDefaultAsync(t => t.ProjectTypeId == id && t.FirmId == firmId);
        return row == null ? NotFound() : row;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectType>> PostProjectType(ProjectType incoming)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var code = NormCode(incoming.Code);
        var name = NormName(incoming.Name);
        if (string.IsNullOrEmpty(code)) return BadRequest("El código es obligatorio.");
        if (string.IsNullOrEmpty(name)) return BadRequest("El nombre es obligatorio.");

        var dup = await _context.ProjectTypes.AnyAsync(t => t.FirmId == firmId && t.Code == code);
        if (dup)
            return Conflict("Ya existe un tipo de proyecto con ese código en la firma.");

        var entity = new ProjectType
        {
            FirmId = firmId.Value,
            Code = code,
            Name = name,
            SortOrder = incoming.SortOrder,
            IsActive = incoming.IsActive
        };
        _context.ProjectTypes.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetProjectType), new { id = entity.ProjectTypeId }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutProjectType(int id, ProjectType incoming)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (id != incoming.ProjectTypeId) return BadRequest();

        var entity = await _context.ProjectTypes.FirstOrDefaultAsync(t => t.ProjectTypeId == id && t.FirmId == firmId);
        if (entity == null) return NotFound();

        var code = NormCode(incoming.Code);
        var name = NormName(incoming.Name);
        if (string.IsNullOrEmpty(code)) return BadRequest("El código es obligatorio.");
        if (string.IsNullOrEmpty(name)) return BadRequest("El nombre es obligatorio.");

        var dup = await _context.ProjectTypes.AnyAsync(t => t.FirmId == firmId && t.Code == code && t.ProjectTypeId != id);
        if (dup)
            return Conflict("Ya existe un tipo de proyecto con ese código en la firma.");

        entity.Code = code;
        entity.Name = name;
        entity.SortOrder = incoming.SortOrder;
        entity.IsActive = incoming.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProjectType(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var entity = await _context.ProjectTypes.FirstOrDefaultAsync(t => t.ProjectTypeId == id && t.FirmId == firmId);
        if (entity == null) return NotFound();

        var inUse = await _context.Projects.AnyAsync(p => p.ProjectTypeId == id);
        if (inUse)
            return Conflict("No se puede eliminar: hay proyectos usando este tipo.");

        _context.ProjectTypes.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
