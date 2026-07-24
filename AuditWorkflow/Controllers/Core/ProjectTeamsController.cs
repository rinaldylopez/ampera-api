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
[Route("api/projects/{projectId:int}/teams")]
[ApiController]
public class ProjectTeamsController(
    AppDbContext context,
    IProfileResourcePermissionService profileResourcePermissions,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<ProjectTeamsController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = profileResourcePermissions;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<ProjectTeamsController> _logger = logger;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectTeam>>> GetProjectTeams(int projectId)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        return await _context.ProjectTeams
            .AsNoTracking()
            .Include(pt => pt.Team)!.ThenInclude(t => t.Customer)
            .Include(pt => pt.Team)!.ThenInclude(t => t.TeamMembers)!.ThenInclude(m => m.User)
            .Where(pt => pt.ProjectId == projectId)
            .ToListAsync();
    }

    public class AddTeamInput
    {
        public int TeamId { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<ProjectTeam>> PostProjectTeam(int projectId, [FromBody] AddTeamInput input, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();
        if (!await TenantQueries.TeamBelongsToFirmAsync(_context, input.TeamId, firmId.Value))
            return BadRequest("El equipo no pertenece a su firma.");
        if (!await TenantQueries.TeamEligibleForProjectAsync(_context, input.TeamId, projectId, firmId.Value))
            return BadRequest(
                "El equipo no corresponde al cliente del proyecto: solo equipos de firma o equipos de ese cliente.");

        var conflictingStaffLabels = await (
                from tm in _context.TeamMembers.AsNoTracking()
                join ps in _context.ProjectStaffs.AsNoTracking() on tm.UserId equals ps.UserId
                join u in _context.Users.AsNoTracking() on ps.UserId equals u.UserId
                where tm.TeamId == input.TeamId && ps.ProjectId == projectId
                select u.FullName ?? u.Email ?? ("Usuario " + u.UserId))
            .Distinct()
            .ToListAsync(cancellationToken);

        if (conflictingStaffLabels.Count > 0)
        {
            var list = string.Join(", ", conflictingStaffLabels.Select(n => $"«{n}»"));
            return Conflict(
                $"No puede vincular este equipo porque estas personas ya están como staff directo del proyecto: {list}. Quite primero el staff individual para esos usuarios o elija otro equipo.");
        }

        var link = new ProjectTeam { ProjectId = projectId, TeamId = input.TeamId };
        _context.ProjectTeams.Add(link);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Este equipo ya está asignado al proyecto.");
        }
        await TrySendProjectTeamAssignedEmailsAsync(projectId, input.TeamId, cancellationToken);
        return CreatedAtAction(nameof(GetProjectTeams), new { projectId }, link);
    }

    [HttpDelete("{projectTeamId:int}")]
    public async Task<IActionResult> DeleteProjectTeam(int projectId, int projectTeamId)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        var row = await _context.ProjectTeams
            .FirstOrDefaultAsync(pt => pt.ProjectTeamId == projectTeamId && pt.ProjectId == projectId);
        if (row == null) return NotFound();

        _context.ProjectTeams.Remove(row);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task TrySendProjectTeamAssignedEmailsAsync(int projectId, int teamId, CancellationToken cancellationToken)
    {
        var data = await _context.Projects
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => new ProjectTeamEmailData(
                p.ProjectCode ?? "",
                p.Name ?? "",
                p.Customer.Name ?? "",
                p.Firm.Name ?? "AMPERA",
                p.AuditPeriod,
                p.ProjectType != null ? p.ProjectType.Name : "",
                p.StartDate))
            .FirstOrDefaultAsync(cancellationToken);

        var team = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new
            {
                TeamName = t.Name ?? "",
                Members = t.TeamMembers
                    .Where(m => m.User.IsActive && m.User.Email != null && m.User.Email != "")
                    .Select(m => new { m.User.FullName, m.User.Email })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (data == null || team == null || team.Members.Count == 0)
            return;

        var projectEsc = WebUtility.HtmlEncode(data.ProjectName);
        var teamEsc = WebUtility.HtmlEncode(team.TeamName);
        var codeEsc = WebUtility.HtmlEncode(data.ProjectCode);
        var customerEsc = WebUtility.HtmlEncode(data.CustomerName);
        var typeEsc = WebUtility.HtmlEncode(data.ProjectTypeName);
        var highlight =
            $"""
            <div style="font-size:20px;font-weight:700;line-height:1.35;color:{AmperaEmailPalette.TealOscuro};">{projectEsc}</div>
            <div style="margin-top:12px;font-size:14px;line-height:1.65;color:{AmperaEmailPalette.TextoSecundario};">
              <strong>Equipo:</strong> {teamEsc}<br />
              <strong>Código:</strong> {codeEsc}<br />
              <strong>Cliente:</strong> {customerEsc}<br />
              <strong>Tipo de proyecto:</strong> {typeEsc}<br />
              <strong>Año fiscal:</strong> {WebUtility.HtmlEncode(data.AuditPeriod.ToString())}<br />
              <strong>Fecha inicio:</strong> {WebUtility.HtmlEncode(FormatDate(data.StartDate))}
            </div>
            """;

        var link = ProjectsLink();
        foreach (var member in team.Members
            .GroupBy(m => m.Email.Trim().ToUpperInvariant())
            .Select(g => g.First()))
        {
            var intro = AmperaBrandedEmail.BodyParagraph(
                $"El equipo <strong>{teamEsc}</strong>, del cual usted forma parte, fue asignado al proyecto <strong>{projectEsc}</strong>.");
            var html = AmperaBrandedEmail.BuildDocument(
                htmlPageTitle: "Equipo asignado a proyecto — AMPERA",
                firmDisplayName: data.FirmName,
                recipientName: member.FullName ?? "",
                introAfterGreetingHtml: intro,
                highlightLabel: "Proyecto asignado",
                highlightInnerHtml: highlight,
                primaryButtonHref: link == "#" ? null : link,
                primaryButtonText: link == "#" ? null : "Ver proyectos",
                fallbackLinkDisplayEncoded: link == "#" ? null : WebUtility.HtmlEncode(link));

            try
            {
                await _emailService.SendHtmlAsync(
                    new[] { member.Email },
                    $"AMPERA — Equipo asignado al proyecto {data.ProjectName}",
                    html,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo enviar correo de equipo {TeamId} asignado al proyecto {ProjectId} para {Email}.",
                    teamId,
                    projectId,
                    member.Email);
            }
        }
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "—";
    }

    private string ProjectsLink()
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/projects";
    }

    private sealed record ProjectTeamEmailData(
        string ProjectCode,
        string ProjectName,
        string CustomerName,
        string FirmName,
        int AuditPeriod,
        string ProjectTypeName,
        DateTime? StartDate);
}
