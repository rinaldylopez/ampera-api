using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class EconomicActivitiesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EconomicActivity>>> GetEconomicActivities()
    {
        return await _context.EconomicActivities
            .AsNoTracking()
            .OrderBy(e => e.Code)
            .ThenBy(e => e.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EconomicActivity>> GetEconomicActivity(int id)
    {
        var row = await _context.EconomicActivities.AsNoTracking().FirstOrDefaultAsync(e => e.EconomicActivityId == id);
        return row == null ? NotFound() : row;
    }

    [HttpPost]
    public async Task<ActionResult<EconomicActivity>> PostEconomicActivity(EconomicActivity row)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name))
            return BadRequest("Código y nombre son obligatorios.");

        row.EconomicActivityId = 0;
        row.Code = row.Code.Trim();
        row.Name = row.Name.Trim();
        _context.EconomicActivities.Add(row);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetEconomicActivity), new { id = row.EconomicActivityId }, row);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutEconomicActivity(int id, EconomicActivity incoming)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != incoming.EconomicActivityId)
            return BadRequest();
        if (string.IsNullOrWhiteSpace(incoming.Code) || string.IsNullOrWhiteSpace(incoming.Name))
            return BadRequest("Código y nombre son obligatorios.");

        var existing = await _context.EconomicActivities.FirstOrDefaultAsync(e => e.EconomicActivityId == id);
        if (existing == null)
            return NotFound();

        existing.Code = incoming.Code.Trim();
        existing.Name = incoming.Name.Trim();
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEconomicActivity(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var row = await _context.EconomicActivities.FindAsync(id);
        if (row == null)
            return NotFound();

        var inUse = await _context.Customers.AsNoTracking().AnyAsync(c => c.EconomicActivityId == id);
        if (inUse)
            return Conflict("La actividad está asignada a uno o más clientes; no se puede eliminar.");

        _context.EconomicActivities.Remove(row);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
