using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CountriesController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Country>>> GetCountries()
    {
        return await _context.Countries
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Country>> GetCountry(int id)
    {
        var country = await _context.Countries.AsNoTracking().FirstOrDefaultAsync(c => c.CountryId == id);
        return country == null ? NotFound() : country;
    }

    [HttpPost]
    public async Task<ActionResult<Country>> PostCountry(Country country)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        country.CountryId = 0;
        country.Name = country.Name?.Trim() ?? string.Empty;
        country.IsoCode = country.IsoCode?.Trim().ToUpperInvariant() ?? string.Empty;
        country.PhonePrefix = country.PhonePrefix?.Trim() ?? string.Empty;
        _context.Countries.Add(country);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCountry), new { id = country.CountryId }, country);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutCountry(int id, Country country)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();
        if (id != country.CountryId)
            return BadRequest();

        country.Name = country.Name?.Trim() ?? string.Empty;
        country.IsoCode = country.IsoCode?.Trim().ToUpperInvariant() ?? string.Empty;
        country.PhonePrefix = country.PhonePrefix?.Trim() ?? string.Empty;
        _context.Entry(country).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Countries.AnyAsync(e => e.CountryId == id))
                return NotFound();
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCountry(int id)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var country = await _context.Countries.FindAsync(id);
        if (country == null)
            return NotFound();

        var inUse = await _context.Customers.AnyAsync(c => c.CountryId == id || c.PhoneCountryId == id)
            || await _context.CustomerContacts.AnyAsync(c => c.PhoneCountryId == id);
        if (inUse)
            return Conflict("El país está asignado a uno o más clientes o prefijos telefónicos; no se puede eliminar.");

        _context.Countries.Remove(country);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
