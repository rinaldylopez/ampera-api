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
[Route("api/projects/{projectId:int}/staff")]
[ApiController]
public class ProjectStaffController(
    AppDbContext context,
    IProfileResourcePermissionService profileResourcePermissions,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<ProjectStaffController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = profileResourcePermissions;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<ProjectStaffController> _logger = logger;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectStaff>>> GetProjectStaff(int projectId)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        return await _context.ProjectStaffs
            .AsNoTracking()
            .Include(ps => ps.User)!.ThenInclude(u => u.Customer)
            .Where(ps => ps.ProjectId == projectId)
            .ToListAsync();
    }

    public class AddStaffInput
    {
        public int UserId { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<ProjectStaff>> PostProjectStaff(int projectId, [FromBody] AddStaffInput input, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        var project = await _context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.FirmId == firmId.Value, cancellationToken);
        if (project == null)
            return NotFound();
        if (!await TenantQueries.UserBelongsToFirmAsync(_context, input.UserId, firmId.Value))
            return BadRequest("El usuario no pertenece a su firma.");
        if (!await TenantQueries.UserEligibleForProjectStaffAsync(_context, input.UserId, projectId, firmId.Value))
            return BadRequest(
                "El usuario no es elegible para este proyecto: solo personal interno o usuarios portal del cliente del encargo.");
        if (project.ManagerId == input.UserId || project.PartnerInChargeId == input.UserId)
            return Conflict("El usuario ya está asignado como manager o socio del proyecto.");

        var teamNamesOverlap = await (
                from pt in _context.ProjectTeams.AsNoTracking()
                join tm in _context.TeamMembers.AsNoTracking() on pt.TeamId equals tm.TeamId
                join t in _context.Teams.AsNoTracking() on pt.TeamId equals t.TeamId
                where pt.ProjectId == projectId && tm.UserId == input.UserId
                select t.Name ?? $"Equipo {t.TeamId}")
            .Distinct()
            .ToListAsync(cancellationToken);

        if (teamNamesOverlap.Count > 0)
        {
            var teamsList = string.Join(", ", teamNamesOverlap.Select(n => $"«{n}»"));
            return Conflict(
                $"Esta persona ya figura como integrante de un equipo ya vinculado al proyecto: {teamsList}. No debe duplicarla como staff directo hasta retirar el equipo correspondiente.");
        }

        var row = new ProjectStaff { ProjectId = projectId, UserId = input.UserId };
        _context.ProjectStaffs.Add(row);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("El usuario ya está asignado al proyecto.");
        }
        await TrySendProjectStaffAssignedEmailAsync(projectId, input.UserId, cancellationToken);
        return CreatedAtAction(nameof(GetProjectStaff), new { projectId }, row);
    }

    [HttpDelete("{projectStaffId:int}")]
    public async Task<IActionResult> DeleteProjectStaff(int projectId, int projectStaffId)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        var row = await _context.ProjectStaffs
            .FirstOrDefaultAsync(ps => ps.ProjectStaffId == projectStaffId && ps.ProjectId == projectId);
        if (row == null) return NotFound();

        _context.ProjectStaffs.Remove(row);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task TrySendProjectStaffAssignedEmailAsync(int projectId, int userId, CancellationToken cancellationToken)
    {
        var data = await _context.Projects
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => new ProjectStaffEmailData(
                p.ProjectCode ?? "",
                p.Name ?? "",
                p.Customer.Name ?? "",
                p.Firm.Name ?? "AMPERA",
                p.AuditPeriod,
                p.ProjectType != null ? p.ProjectType.Name : "",
                p.StartDate))
            .FirstOrDefaultAsync(cancellationToken);

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (data == null || user == null || string.IsNullOrWhiteSpace(user.Email))
            return;

        var projectEsc = WebUtility.HtmlEncode(data.ProjectName);
        var codeEsc = WebUtility.HtmlEncode(data.ProjectCode);
        var customerEsc = WebUtility.HtmlEncode(data.CustomerName);
        var typeEsc = WebUtility.HtmlEncode(data.ProjectTypeName);
        var highlight =
            $"""
            <div style="font-size:20px;font-weight:700;line-height:1.35;color:{AmperaEmailPalette.TealOscuro};">{projectEsc}</div>
            <div style="margin-top:12px;font-size:14px;line-height:1.65;color:{AmperaEmailPalette.TextoSecundario};">
              <strong>Código:</strong> {codeEsc}<br />
              <strong>Cliente:</strong> {customerEsc}<br />
              <strong>Tipo de proyecto:</strong> {typeEsc}<br />
              <strong>Año fiscal:</strong> {WebUtility.HtmlEncode(data.AuditPeriod.ToString())}<br />
              <strong>Fecha inicio:</strong> {WebUtility.HtmlEncode(FormatDate(data.StartDate))}
            </div>
            """;

        var intro = AmperaBrandedEmail.BodyParagraph(
            $"Ha sido asignado como <strong>staff directo</strong> al proyecto <strong>{projectEsc}</strong>.");
        var link = ProjectsLink();
        var html = AmperaBrandedEmail.BuildDocument(
            htmlPageTitle: "Asignación a proyecto — AMPERA",
            firmDisplayName: data.FirmName,
            recipientName: user.FullName ?? "",
            introAfterGreetingHtml: intro,
            highlightLabel: "Proyecto asignado",
            highlightInnerHtml: highlight,
            primaryButtonHref: link == "#" ? null : link,
            primaryButtonText: link == "#" ? null : "Ver proyectos",
            fallbackLinkDisplayEncoded: link == "#" ? null : WebUtility.HtmlEncode(link));

        try
        {
            await _emailService.SendHtmlAsync(
                new[] { user.Email },
                $"AMPERA — Asignación al proyecto {data.ProjectName}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar correo de staff del proyecto {ProjectId} al usuario {UserId}.", projectId, userId);
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

    private sealed record ProjectStaffEmailData(
        string ProjectCode,
        string ProjectName,
        string CustomerName,
        string FirmName,
        int AuditPeriod,
        string ProjectTypeName,
        DateTime? StartDate);
}
