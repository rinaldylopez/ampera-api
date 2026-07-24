using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CustomersController(AppDbContext context, IProfileResourcePermissionService profileResourcePermissions) :
    ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = profileResourcePermissions;
    private static readonly string[] ValidCustomerCategories = ["Grandes", "Medianos", "Otros"];

    private static string NormalizeCustomerCategory(string category)
    {
        var value = (category ?? string.Empty).Trim();
        return ValidCustomerCategories.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ?? "Otros";
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Country)
            .Include(c => c.PhoneCountry)
            .Include(c => c.EconomicActivity)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.ContactType)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.PhoneCountry)
            .Where(c => c.FirmId == firmId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Listado paginado y filtrable (catálogos / equipos siguen usando GET sin paginar).
    /// </summary>
    [HttpGet("paged")]
    public async Task<ActionResult<object>> GetCustomersPaged(
        int page = 1,
        int pageSize = 10,
        string search = "")
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var skip = (page - 1) * pageSize;
        var term = search?.Trim() ?? "";

        var query = _context.Customers
            .AsNoTracking()
            .Include(c => c.Country)
            .Include(c => c.PhoneCountry)
            .Include(c => c.EconomicActivity)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.ContactType)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.PhoneCountry)
            .Where(c => c.FirmId == firmId);

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(c =>
                c.Name.Contains(term) ||
                (c.TaxId != null && c.TaxId.Contains(term)) ||
                (c.RegistrationNumber != null && c.RegistrationNumber.Contains(term)) ||
                (c.Email != null && c.Email.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.Address != null && c.Address.Contains(term)) ||
                (c.Category != null && c.Category.Contains(term)) ||
                (c.BusinessLine != null && c.BusinessLine.Contains(term)) ||
                (c.Country != null && c.Country.Name.Contains(term)) ||
                (c.EconomicActivity != null && c.EconomicActivity.Code.Contains(term)) ||
                (c.EconomicActivity != null && c.EconomicActivity.Name.Contains(term)));
        }

        var totalRecords = await query.CountAsync();

        var customers = await query
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            TotalRecords = totalRecords,
            Customers = customers
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Customer>> GetCustomer(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var customer = await _context.Customers
            .AsNoTracking()
            .Include(c => c.Country)
            .Include(c => c.PhoneCountry)
            .Include(c => c.EconomicActivity)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.ContactType)
            .Include(c => c.Contacts)
                .ThenInclude(c => c.PhoneCountry)
            .FirstOrDefaultAsync(c => c.CustomerId == id && c.FirmId == firmId);

        return customer == null ? NotFound() : customer;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutCustomer(int id, Customer incoming)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Customers,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (id != incoming.CustomerId)
            return BadRequest();

        var existing = await _context.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.CustomerId == id && c.FirmId == firmId);
        if (existing == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(incoming.Name))
            return BadRequest("El nombre es obligatorio.");

        if (!await _context.Countries.AsNoTracking().AnyAsync(c => c.CountryId == incoming.CountryId))
            return BadRequest("País no válido.");
        if (incoming.PhoneCountryId is int phoneCountryId && phoneCountryId > 0 &&
            !await _context.Countries.AsNoTracking().AnyAsync(c => c.CountryId == phoneCountryId))
            return BadRequest("Prefijo telefónico no válido.");

        var contactResult = await NormalizeContactsAsync(incoming.Contacts);
        if (!string.IsNullOrEmpty(contactResult.Error))
            return BadRequest(contactResult.Error);

        if (incoming.EconomicActivityId is int eid && eid > 0)
        {
            var activity = await _context.EconomicActivities.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EconomicActivityId == eid);
            if (activity == null)
                return BadRequest("Actividad económica no válida.");
            existing.EconomicActivityId = eid;
            existing.BusinessLine = activity.Name;
        }
        else
        {
            existing.EconomicActivityId = null;
            existing.BusinessLine = string.Empty;
        }

        existing.Name = incoming.Name.Trim();
        existing.TaxId = incoming.TaxId?.Trim() ?? string.Empty;
        existing.RegistrationNumber = incoming.RegistrationNumber?.Trim() ?? string.Empty;
        existing.Address = incoming.Address?.Trim() ?? string.Empty;
        existing.Category = NormalizeCustomerCategory(incoming.Category);
        existing.Email = incoming.Email?.Trim() ?? string.Empty;
        existing.PhoneCountryId = incoming.PhoneCountryId is > 0 ? incoming.PhoneCountryId : null;
        existing.Phone = incoming.Phone?.Trim() ?? string.Empty;
        existing.CountryId = incoming.CountryId;
        existing.IsActive = incoming.IsActive;
        SyncCustomerContacts(existing, contactResult.Contacts);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Customers.AnyAsync(e => e.CustomerId == id && e.FirmId == firmId))
                return NotFound();
            throw;
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> PostCustomer(Customer customer)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Customers,
                ResourcePermissionAction.Create))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        if (string.IsNullOrWhiteSpace(customer.Name))
            return BadRequest("El nombre es obligatorio.");

        if (!await _context.Countries.AsNoTracking().AnyAsync(c => c.CountryId == customer.CountryId))
            return BadRequest("País no válido.");
        if (customer.PhoneCountryId is int phoneCountryId && phoneCountryId > 0 &&
            !await _context.Countries.AsNoTracking().AnyAsync(c => c.CountryId == phoneCountryId))
            return BadRequest("Prefijo telefónico no válido.");

        var contactResult = await NormalizeContactsAsync(customer.Contacts);
        if (!string.IsNullOrEmpty(contactResult.Error))
            return BadRequest(contactResult.Error);

        if (customer.EconomicActivityId is int neid && neid > 0)
        {
            var activity = await _context.EconomicActivities.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EconomicActivityId == neid);
            if (activity == null)
                return BadRequest("Actividad económica no válida.");
            customer.EconomicActivityId = neid;
            customer.BusinessLine = activity.Name;
        }
        else
        {
            customer.EconomicActivityId = null;
            customer.BusinessLine = string.Empty;
        }

        customer.CustomerId = 0;
        customer.FirmId = firmId.Value;
        customer.Name = customer.Name.Trim();
        customer.TaxId = customer.TaxId?.Trim() ?? string.Empty;
        customer.RegistrationNumber = customer.RegistrationNumber?.Trim() ?? string.Empty;
        customer.Address = customer.Address?.Trim() ?? string.Empty;
        customer.Category = NormalizeCustomerCategory(customer.Category);
        customer.Email = customer.Email?.Trim() ?? string.Empty;
        customer.PhoneCountryId = customer.PhoneCountryId is > 0 ? customer.PhoneCountryId : null;
        customer.Phone = customer.Phone?.Trim() ?? string.Empty;
        customer.Contacts = contactResult.Contacts;

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCustomer), new { id = customer.CustomerId }, customer);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Customers,
                ResourcePermissionAction.Delete))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == id && c.FirmId == firmId);
        if (customer == null) return NotFound();

        var hasProjects = await _context.Projects.AsNoTracking().AnyAsync(p => p.CustomerId == id);
        var hasUsers = await _context.Users.AsNoTracking().AnyAsync(u => u.CustomerId == id)
            || await _context.UserCustomerMemberships.AsNoTracking().AnyAsync(m => m.CustomerId == id);
        if (hasProjects || hasUsers)
            return Conflict("El cliente tiene proyectos o usuarios asociados; no se puede eliminar.");

        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<CustomerContactsNormalizeResult> NormalizeContactsAsync(IEnumerable<CustomerContact> contacts)
    {
        var incomingContacts = contacts ?? [];
        var normalized = incomingContacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Name)
                || !string.IsNullOrWhiteSpace(c.Phone)
                || !string.IsNullOrWhiteSpace(c.Email))
            .Select(c => new CustomerContact
            {
                CustomerContactId = 0,
                CustomerContactTypeId = c.CustomerContactTypeId,
                PhoneCountryId = c.PhoneCountryId is > 0 ? c.PhoneCountryId : null,
                Name = c.Name?.Trim() ?? string.Empty,
                Phone = c.Phone?.Trim() ?? string.Empty,
                Email = c.Email?.Trim() ?? string.Empty
            })
            .ToList();

        foreach (var contact in normalized)
        {
            if (string.IsNullOrWhiteSpace(contact.Name))
                return new CustomerContactsNormalizeResult("El nombre del contacto es obligatorio.", []);
            if (contact.CustomerContactTypeId <= 0)
                return new CustomerContactsNormalizeResult("El tipo de contacto es obligatorio.", []);
        }

        var typeIds = normalized.Select(c => c.CustomerContactTypeId).Distinct().ToList();
        var phoneCountryIds = normalized
            .Where(c => c.PhoneCountryId is > 0)
            .Select(c => c.PhoneCountryId!.Value)
            .Distinct()
            .ToList();
        if (typeIds.Count > 0)
        {
            var validTypes = await _context.CustomerContactTypes
                .AsNoTracking()
                .Where(t => t.IsActive && typeIds.Contains(t.CustomerContactTypeId))
                .Select(t => t.CustomerContactTypeId)
                .ToListAsync();

            if (validTypes.Count != typeIds.Count)
                return new CustomerContactsNormalizeResult("Tipo de contacto no válido.", []);
        }

        if (phoneCountryIds.Count > 0)
        {
            var validCountries = await _context.Countries
                .AsNoTracking()
                .Where(c => phoneCountryIds.Contains(c.CountryId))
                .Select(c => c.CountryId)
                .ToListAsync();

            if (validCountries.Count != phoneCountryIds.Count)
                return new CustomerContactsNormalizeResult("Prefijo telefónico de contacto no válido.", []);
        }

        return new CustomerContactsNormalizeResult("", normalized);
    }

    private void SyncCustomerContacts(Customer customer, List<CustomerContact> contacts)
    {
        _context.CustomerContacts.RemoveRange(customer.Contacts);
        customer.Contacts.Clear();
        foreach (var contact in contacts)
        {
            contact.CustomerId = customer.CustomerId;
            customer.Contacts.Add(contact);
        }
    }

    private sealed record CustomerContactsNormalizeResult(string Error, List<CustomerContact> Contacts);
}
