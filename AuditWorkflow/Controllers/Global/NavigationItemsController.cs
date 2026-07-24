using AuditWorkflow.Data;
using AuditWorkflow.Models.Global;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class NavigationItemsController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    /// <summary>
    /// Catálogo plano de ítems de navegación (para asignar a perfiles).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NavigationItemCatalogDto>>> GetCatalog()
    {
        return await _context.NavigationItems
            .AsNoTracking()
            .Select(n => new NavigationItemCatalogDto
            {
                NavigationItemId = n.NavigationItemId,
                ModuleId = n.ModuleId,
                ModuleName = n.Module.Name,
                Name = n.Name,
                Path = n.Path
            })
            .OrderBy(x => x.ModuleName)
            .ThenBy(x => x.Name)
            .ToListAsync();
    }
}
