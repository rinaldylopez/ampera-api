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
public class FirmsController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Firm>>> GetFirms()
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        if (User.IsInRole("Admin"))
        {
            return await _context.Firms
                .AsNoTracking()
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        return await _context.Firms
            .AsNoTracking()
            .Where(f => f.FirmId == firmId)
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Firm>> GetFirm(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (id != firmId && !User.IsInRole("Admin"))
            return Forbid();

        var firm = await _context.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.FirmId == id);
        return firm == null ? NotFound() : firm;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutFirm(int id, Firm firm)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (id != firm.FirmId) return BadRequest();
        if (!User.IsInRole("Admin") && id != firmId) return Forbid();

        _context.Entry(firm).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Firms.AnyAsync(e => e.FirmId == id))
                return NotFound();
            throw;
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Firm>> PostFirm(Firm firm)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        _context.Firms.Add(firm);
        await _context.SaveChangesAsync();

        foreach (var row in DefaultProjectTypesForFirm.CreateSeedRows(firm.FirmId))
            _context.ProjectTypes.Add(row);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetFirm), new { id = firm.FirmId }, firm);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteFirm(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var firm = await _context.Firms.FindAsync(id);
        if (firm == null) return NotFound();

        _context.Firms.Remove(firm);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
