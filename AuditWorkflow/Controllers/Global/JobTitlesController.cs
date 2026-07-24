using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class JobTitlesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobTitle>>> GetJobTitles()
    {
        return await _context.JobTitle
            .AsNoTracking()
            .OrderBy(j => j.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobTitle>> GetJobTitle(int id)
    {
        var row = await _context.JobTitle.AsNoTracking().FirstOrDefaultAsync(j => j.JobTitleId == id);
        return row == null ? NotFound() : row;
    }

    [HttpPost]
    public async Task<ActionResult<JobTitle>> PostJobTitle(JobTitle jobTitle)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var name = (jobTitle.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest("El nombre es obligatorio.");
        if (name.Length > 100)
            return BadRequest("El nombre no puede superar 100 caracteres.");

        var desc = (jobTitle.Description ?? "").Trim();
        if (desc.Length > 250)
            return BadRequest("La descripción no puede superar 250 caracteres.");

        if (await NameTakenAsync(name, excludeId: 0))
            return Conflict("Ya existe un puesto con ese nombre.");

        jobTitle.JobTitleId = 0;
        jobTitle.Name = name;
        jobTitle.Description = desc;

        _context.JobTitle.Add(jobTitle);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetJobTitle), new { id = jobTitle.JobTitleId }, jobTitle);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutJobTitle(int id, JobTitle jobTitle)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != jobTitle.JobTitleId)
            return BadRequest();

        var name = (jobTitle.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest("El nombre es obligatorio.");
        if (name.Length > 100)
            return BadRequest("El nombre no puede superar 100 caracteres.");

        var desc = (jobTitle.Description ?? "").Trim();
        if (desc.Length > 250)
            return BadRequest("La descripción no puede superar 250 caracteres.");

        if (await NameTakenAsync(name, excludeId: id))
            return Conflict("Ya existe un puesto con ese nombre.");

        var entity = await _context.JobTitle.FindAsync(id);
        if (entity == null)
            return NotFound();

        entity.Name = name;
        entity.Description = desc;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.JobTitle.AnyAsync(e => e.JobTitleId == id))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteJobTitle(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var row = await _context.JobTitle.FindAsync(id);
        if (row == null)
            return NotFound();

        var inUse = await _context.Users.AnyAsync(u => u.JobTitleId == id);
        if (inUse)
            return Conflict("El puesto está asignado a uno o más usuarios; no se puede eliminar.");

        _context.JobTitle.Remove(row);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> NameTakenAsync(string name, int excludeId)
    {
        var n = name.Trim();
        return await _context.JobTitle
            .AsNoTracking()
            .AnyAsync(j => j.JobTitleId != excludeId && j.Name.ToLower() == n.ToLower());
    }
}
