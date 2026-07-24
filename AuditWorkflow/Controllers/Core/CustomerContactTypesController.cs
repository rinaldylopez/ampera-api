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
public class CustomerContactTypesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    private static string NormCode(string code) => (code ?? string.Empty).Trim();
    private static string NormName(string name) => (name ?? string.Empty).Trim();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerContactType>>> GetCustomerContactTypes([FromQuery] bool includeInactive = false)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (includeInactive && !User.IsInRole("Admin"))
            return Forbid();

        var query = _context.CustomerContactTypes
            .AsNoTracking()
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(t => t.IsActive);

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerContactType>> GetCustomerContactType(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var row = await _context.CustomerContactTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CustomerContactTypeId == id);
        return row == null ? NotFound() : row;
    }

    [HttpPost]
    public async Task<ActionResult<CustomerContactType>> PostCustomerContactType(CustomerContactType incoming)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var code = NormCode(incoming.Code);
        var name = NormName(incoming.Name);
        if (string.IsNullOrWhiteSpace(code)) return BadRequest("El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("El nombre es obligatorio.");

        var dup = await _context.CustomerContactTypes.AnyAsync(t => t.Code == code);
        if (dup)
            return Conflict("Ya existe un tipo de contacto con ese código.");

        var entity = new CustomerContactType
        {
            Code = code,
            Name = name,
            SortOrder = incoming.SortOrder,
            IsActive = incoming.IsActive
        };
        _context.CustomerContactTypes.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCustomerContactType), new { id = entity.CustomerContactTypeId }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutCustomerContactType(int id, CustomerContactType incoming)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != incoming.CustomerContactTypeId) return BadRequest();

        var entity = await _context.CustomerContactTypes.FirstOrDefaultAsync(t => t.CustomerContactTypeId == id);
        if (entity == null) return NotFound();

        var code = NormCode(incoming.Code);
        var name = NormName(incoming.Name);
        if (string.IsNullOrWhiteSpace(code)) return BadRequest("El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("El nombre es obligatorio.");

        var dup = await _context.CustomerContactTypes.AnyAsync(t => t.Code == code && t.CustomerContactTypeId != id);
        if (dup)
            return Conflict("Ya existe un tipo de contacto con ese código.");

        entity.Code = code;
        entity.Name = name;
        entity.SortOrder = incoming.SortOrder;
        entity.IsActive = incoming.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomerContactType(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var entity = await _context.CustomerContactTypes.FirstOrDefaultAsync(t => t.CustomerContactTypeId == id);
        if (entity == null) return NotFound();

        var inUse = await _context.CustomerContacts.AnyAsync(c => c.CustomerContactTypeId == id);
        if (inUse)
            return Conflict("No se puede eliminar: hay contactos usando este tipo. Puede desactivarlo.");

        _context.CustomerContactTypes.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
