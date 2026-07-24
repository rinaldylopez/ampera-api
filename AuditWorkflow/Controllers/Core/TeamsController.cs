using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class TeamsController(
    AppDbContext context,
    IProfileResourcePermissionService profileResourcePermissions,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<TeamsController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = profileResourcePermissions;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<TeamsController> _logger = logger;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Team>>> GetTeams()
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        return await _context.Teams
            .AsNoTracking()
            .Include(t => t.Customer)
            .Where(t => t.FirmId == firmId)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    [HttpGet("paged")]
    public async Task<ActionResult<object>> GetTeamsPaged(
        int page = 1,
        int pageSize = 10,
        string search = "",
        bool firmWideOnly = false,
        int? filterByCustomerId = null)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var skip = (page - 1) * pageSize;
        var term = search?.Trim() ?? "";

        var query = _context.Teams
            .AsNoTracking()
            .Include(t => t.Customer)
            .Where(t => t.FirmId == firmId);

        if (filterByCustomerId is int cid && cid > 0)
            query = query.Where(t => t.CustomerId == cid);
        else if (firmWideOnly)
            query = query.Where(t => t.CustomerId == null);

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(t =>
                t.Name.Contains(term) ||
                (t.Description != null && t.Description.Contains(term)) ||
                (t.Customer != null && t.Customer.Name.Contains(term)));
        }

        var totalRecords = await query.CountAsync();

        var teams = await query
            .OrderBy(t => t.Name)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            TotalRecords = totalRecords,
            Teams = teams
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Team>> GetTeam(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var team = await _context.Teams
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.TeamMembers)!.ThenInclude(m => m.User)!.ThenInclude(u => u.Customer)
            .FirstOrDefaultAsync(t => t.TeamId == id && t.FirmId == firmId);

        return team == null ? NotFound() : team;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutTeam(int id, Team incoming)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Teams,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (id != incoming.TeamId)
            return BadRequest();

        var existing = await _context.Teams.FirstOrDefaultAsync(t => t.TeamId == id && t.FirmId == firmId);
        if (existing == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(incoming.Name))
            return BadRequest("El nombre es obligatorio.");

        if (incoming.CustomerId is int cid && !await TenantQueries.CustomerBelongsToFirmAsync(_context, cid, firmId.Value))
            return BadRequest("El cliente no pertenece a su firma.");

        existing.Name = incoming.Name.Trim();
        existing.Description = incoming.Description?.Trim() ?? string.Empty;
        existing.IsActive = incoming.IsActive;
        existing.CustomerId = incoming.CustomerId;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Teams.AnyAsync(e => e.TeamId == id && e.FirmId == firmId))
                return NotFound();
            throw;
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Team>> PostTeam(Team team)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Teams,
                ResourcePermissionAction.Create))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        if (string.IsNullOrWhiteSpace(team.Name))
            return BadRequest("El nombre es obligatorio.");

        if (team.CustomerId is int cid && !await TenantQueries.CustomerBelongsToFirmAsync(_context, cid, firmId.Value))
            return BadRequest("El cliente no pertenece a su firma.");

        team.TeamId = 0;
        team.FirmId = firmId.Value;
        team.Name = team.Name.Trim();
        team.Description = team.Description?.Trim() ?? string.Empty;

        _context.Teams.Add(team);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTeam), new { id = team.TeamId }, team);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTeam(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Teams,
                ResourcePermissionAction.Delete))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        var team = await _context.Teams.FirstOrDefaultAsync(t => t.TeamId == id && t.FirmId == firmId);
        if (team == null) return NotFound();

        var inProjects = await _context.ProjectTeams.AsNoTracking().AnyAsync(pt => pt.TeamId == id);
        if (inProjects)
            return Conflict("El equipo está asignado a uno o más proyectos; quítelo en los proyectos antes de eliminar.");

        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Agrega un usuario al equipo; elegibilidad según equipo de firma o de cliente.</summary>
    [HttpPost("{teamId:int}/members")]
    public async Task<ActionResult<TeamMember>> PostTeamMember(
        int teamId,
        [FromBody] TeamMemberInput input,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Teams,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (!await TenantQueries.TeamBelongsToFirmAsync(_context, teamId, firmId.Value))
            return NotFound();
        if (!await TenantQueries.UserEligibleForTeamMembershipAsync(_context, teamId, input.UserId, firmId.Value))
            return BadRequest(
                "El usuario no es elegible para este equipo: equipos de firma solo usuarios internos; equipos de cliente solo usuarios portal de ese cliente.");

        if (await _context.TeamMembers.AnyAsync(
                m => m.TeamId == teamId && m.UserId == input.UserId,
                cancellationToken))
            return Conflict("El usuario ya está en este equipo.");

        // Client users may belong to several customer-scoped teams (one per accessible customer).
        var member = new TeamMember
        {
            TeamId = teamId,
            UserId = input.UserId,
            IsLeader = input.IsLeader,
            AssignedAt = DateTime.UtcNow
        };
        _context.TeamMembers.Add(member);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("El usuario ya está en este equipo.");
        }

        await TrySendTeamMemberEmailAsync(teamId, input.UserId, input.IsLeader, TeamMemberEmailEvent.Assigned, cancellationToken);
        return CreatedAtAction(nameof(GetTeam), new { id = teamId }, member);
    }

    [HttpDelete("{teamId:int}/members/{userId:int}")]
    public async Task<IActionResult> DeleteTeamMember(int teamId, int userId, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Teams,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (!await TenantQueries.TeamBelongsToFirmAsync(_context, teamId, firmId.Value))
            return NotFound();

        var member = await _context.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);
        if (member == null) return NotFound();

        var wasLeader = member.IsLeader;

        _context.TeamMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);
        await TrySendTeamMemberEmailAsync(teamId, userId, wasLeader, TeamMemberEmailEvent.Removed, cancellationToken);
        return NoContent();
    }

    public class TeamMemberInput
    {
        public int UserId { get; set; }
        public bool IsLeader { get; set; }
    }

    private enum TeamMemberEmailEvent
    {
        Assigned,
        Removed
    }

    private async Task TrySendTeamMemberEmailAsync(
        int teamId,
        int userId,
        bool isLeader,
        TeamMemberEmailEvent emailEvent,
        CancellationToken cancellationToken)
    {
        var data = await LoadTeamMemberEmailDataAsync(teamId, userId, cancellationToken);
        if (data == null || string.IsNullOrWhiteSpace(data.UserEmail))
            return;

        var teamEsc = WebUtility.HtmlEncode(data.TeamName.Trim());
        var customerPlain = data.CustomerName?.Trim();
        var contextLabel = string.IsNullOrWhiteSpace(customerPlain) ? "Firma" : "Cliente";
        var contextValue = string.IsNullOrWhiteSpace(customerPlain)
            ? data.FirmName.Trim()
            : customerPlain;
        var contextValueEsc = WebUtility.HtmlEncode(contextValue);
        var contextLabelEsc = WebUtility.HtmlEncode(contextLabel);
        var memberRoleEsc = WebUtility.HtmlEncode(isLeader ? "Líder del equipo" : "Miembro del equipo");
        var isRemoval = emailEvent == TeamMemberEmailEvent.Removed;
        var actionText = isRemoval ? "removido del" : "agregado al";
        var pageTitle = isRemoval ? "Remoción de equipo — AMPERA" : "Asignación a equipo — AMPERA";
        var highlightLabel = isRemoval ? "Equipo removido" : "Equipo asignado";
        var subjectAction = isRemoval ? "Removido del equipo" : "Agregado al equipo";
        var roleLabel = isRemoval ? "Rol anterior" : "Rol";

        var highlight =
            $"""
            <div style="font-size:20px;font-weight:700;line-height:1.35;color:{AmperaEmailPalette.TealOscuro};">{teamEsc}</div>
            <div style="margin-top:12px;font-size:14px;line-height:1.6;color:{AmperaEmailPalette.TextoSecundario};">
              <strong>{contextLabelEsc}:</strong> {contextValueEsc}<br />
              <strong>{WebUtility.HtmlEncode(roleLabel)}:</strong> {memberRoleEsc}
            </div>
            """;

        var intro = AmperaBrandedEmail.BodyParagraph(
            $"Ha sido <strong>{WebUtility.HtmlEncode(actionText)}</strong> equipo <strong>{teamEsc}</strong>. " +
            $"Este equipo corresponde a <strong>{WebUtility.HtmlEncode(contextLabel.ToLowerInvariant())}</strong>: <strong>{contextValueEsc}</strong>.");

        var link = TeamsLink();
        var safeLink = link == "#" ? null : WebUtility.HtmlEncode(link);
        var html = AmperaBrandedEmail.BuildDocument(
            htmlPageTitle: pageTitle,
            firmDisplayName: data.FirmName,
            recipientName: data.UserName,
            introAfterGreetingHtml: intro,
            highlightLabel: highlightLabel,
            highlightInnerHtml: highlight,
            primaryButtonHref: link == "#" ? null : link,
            primaryButtonText: link == "#" ? null : "Ver equipos",
            fallbackLinkDisplayEncoded: safeLink);

        try
        {
            await _emailService.SendHtmlAsync(
                new[] { data.UserEmail },
                $"AMPERA — {subjectAction} {data.TeamName}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo enviar el correo de {Event} del equipo {TeamId} para el usuario {UserId}.",
                emailEvent,
                teamId,
                userId);
        }
    }

    private async Task<TeamMemberEmailData> LoadTeamMemberEmailDataAsync(
        int teamId,
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
            return null;

        var team = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new
            {
                TeamName = t.Name ?? "",
                FirmName = t.Firm.Name ?? "AMPERA",
                CustomerName = t.Customer != null ? t.Customer.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (team == null)
            return null;

        return new TeamMemberEmailData(
            user.FullName ?? "",
            user.Email ?? "",
            team.TeamName,
            team.FirmName,
            team.CustomerName);
    }

    private sealed record TeamMemberEmailData(
        string UserName,
        string UserEmail,
        string TeamName,
        string FirmName,
        string CustomerName);

    private string TeamsLink()
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/teams";
    }
}
