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
public class ProjectsController(
    AppDbContext context,
    IProfileResourcePermissionService profileResourcePermissions,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<ProjectsController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = profileResourcePermissions;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<ProjectsController> _logger = logger;

    private static int? NormalizeOptionalProjectTypeId(int? raw) => raw is > 0 ? raw : null;

    private static int? NormalizeOptionalPartnerInChargeId(int? raw) => raw is > 0 ? raw : null;

    private static int? NormalizeDefaultReminderDays(int? raw) => raw is >= 1 and <= 365 ? raw : null;

    private async Task<bool> ProjectTypeExistsForFirmAsync(int firmId, int? projectTypeId)
    {
        if (projectTypeId is null) return true;
        return await _context.ProjectTypes.AsNoTracking().AnyAsync(pt => pt.ProjectTypeId == projectTypeId.Value && pt.FirmId == firmId);
    }

    private async Task<bool> ProjectResponsibleUserAllowedAsync(int userId, int firmId, int customerId)
    {
        return await _context.Users.AsNoTracking().AnyAsync(u =>
            u.UserId == userId
            && u.IsActive
            && (
                (u.UserType == TenantQueries.UserTypeFirm
                    && _context.UserFirmMemberships.Any(m => m.UserId == u.UserId && m.FirmId == firmId))
                || (u.UserType == TenantQueries.UserTypeClient
                    && u.FirmId == firmId
                    && (u.CustomerId == customerId
                        || _context.UserCustomerMemberships.Any(m => m.UserId == u.UserId && m.CustomerId == customerId)))
            ));
    }

    private async Task<string> ValidateProjectWriteAsync(Project project, int firmId)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectCode) || string.IsNullOrWhiteSpace(project.Name))
            return "Código y nombre son obligatorios.";
        if (project.ProjectCode.Trim().Length > 100)
            return "El código no puede exceder 100 caracteres.";
        if (string.IsNullOrWhiteSpace(project.Status))
            return "Estado es obligatorio.";
        if (project.AuditPeriod <= 0)
            return "Año fiscal es obligatorio.";
        if (project.ProjectTypeId is not > 0)
            return "Tipo de proyecto es obligatorio.";
        if (project.CustomerId <= 0 || !await TenantQueries.CustomerBelongsToFirmAsync(_context, project.CustomerId, firmId))
            return "Cliente inválido para su firma.";
        if (project.ManagerId <= 0 || !await ProjectResponsibleUserAllowedAsync(project.ManagerId, firmId, project.CustomerId))
            return "El manager debe pertenecer a su firma o al cliente seleccionado.";
        if (project.PartnerInChargeId is not > 0 || !await ProjectResponsibleUserAllowedAsync(project.PartnerInChargeId.Value, firmId, project.CustomerId))
            return "El socio a cargo debe pertenecer a su firma o al cliente seleccionado.";
        if (project.BudgetedHours < 0)
            return "Horas presupuestadas no puede ser negativa.";
        if (project.DefaultReminderDaysBeforeDue is not int reminderDays || reminderDays < 1 || reminderDays > 365)
            return "Los días de recordatorio por defecto son obligatorios (1–365).";
        if (project.StartDate is null)
            return "Fecha de inicio es obligatoria.";
        if (project.EndDate is null)
            return "Fecha de finalización es obligatoria.";
        if (project.EndDate.Value.Date < project.StartDate.Value.Date)
            return "La fecha fin no puede ser anterior a la fecha de inicio.";
        if (!await ProjectTypeExistsForFirmAsync(firmId, project.ProjectTypeId))
            return "Tipo de proyecto inválido.";

        return "";
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Project>>> GetProjects()
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var q = _context.Projects
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Manager)
            .Include(p => p.PartnerInCharge)
            .Include(p => p.ProjectType)
            .Include(p => p.ProjectTeams)!
                .ThenInclude(pt => pt.Team)
                    .ThenInclude(t => t.TeamMembers)!
                        .ThenInclude(tm => tm.User)
            .Include(p => p.ProjectStaffs)!
                .ThenInclude(ps => ps.User)
            .Where(p => p.FirmId == firmId);

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
        {
            var cc = User.GetCustomerId();
            if (cc is null)
                return Unauthorized();
            q = q.Where(p => p.CustomerId == cc.Value);
        }

        return await q.OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    [HttpGet("by-customer/{customerId:int}")]
    public async Task<ActionResult<IEnumerable<Project>>> GetProjectsByCustomer(int customerId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
        {
            if (User.GetCustomerId() is not int myCustomerId || myCustomerId != customerId)
                return Forbid();
        }

        if (!await TenantQueries.CustomerBelongsToFirmAsync(_context, customerId, firmId.Value))
            return NotFound();

        return await _context.Projects
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Manager)
            .Include(p => p.PartnerInCharge)
            .Include(p => p.ProjectType)
            .Include(p => p.ProjectTeams)!
                .ThenInclude(pt => pt.Team)
                    .ThenInclude(t => t.TeamMembers)!
                        .ThenInclude(tm => tm.User)
            .Include(p => p.ProjectStaffs)!
                .ThenInclude(ps => ps.User)
            .Where(p => p.FirmId == firmId && p.CustomerId == customerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Project>> GetProject(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var project = await _context.Projects
            .AsNoTracking()
            .Include(p => p.Firm)
            .Include(p => p.Customer)
            .Include(p => p.Manager)
            .Include(p => p.PartnerInCharge)
            .Include(p => p.ProjectType)
            .Include(p => p.ProjectTeams)!
                .ThenInclude(pt => pt.Team)
            .Include(p => p.ProjectStaffs)!
                .ThenInclude(ps => ps.User)
            .FirstOrDefaultAsync(p => p.ProjectId == id && p.FirmId == firmId);

        if (project == null) return NotFound();
        if (!await TenantQueries.ProjectAccessibleToUserAsync(_context, id, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return project;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutProject(int id, Project incoming, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Update))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });
        if (id != incoming.ProjectId)
            return BadRequest();

        var existing = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == id && p.FirmId == firmId);
        if (existing == null)
            return NotFound();

        var validationError = await ValidateProjectWriteAsync(incoming, firmId.Value);
        if (!string.IsNullOrEmpty(validationError))
            return BadRequest(validationError);

        var wantTypeId = NormalizeOptionalProjectTypeId(incoming.ProjectTypeId);
        var wantPartner = NormalizeOptionalPartnerInChargeId(incoming.PartnerInChargeId);
        var previousManagerId = existing.ManagerId;
        var previousPartnerId = existing.PartnerInChargeId;

        existing.ProjectCode = incoming.ProjectCode.Trim();
        existing.Name = incoming.Name.Trim();
        existing.Status = incoming.Status?.Trim() ?? "Active";
        existing.AuditPeriod = incoming.AuditPeriod;
        existing.ProjectTypeId = wantTypeId;
        existing.CustomerId = incoming.CustomerId;
        existing.ManagerId = incoming.ManagerId;
        existing.PartnerInChargeId = wantPartner;
        existing.BudgetedHours = incoming.BudgetedHours;
        existing.DefaultReminderDaysBeforeDue = NormalizeDefaultReminderDays(incoming.DefaultReminderDaysBeforeDue);
        existing.StartDate = incoming.StartDate;
        existing.EndDate = incoming.EndDate;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Projects.AnyAsync(e => e.ProjectId == id && e.FirmId == firmId))
                return NotFound();
            throw;
        }

        var managerChanged = previousManagerId != incoming.ManagerId;
        var partnerChanged = previousPartnerId != wantPartner;
        if (managerChanged || partnerChanged)
        {
            await TrySendProjectSavedEmailAsync(
                existing.ProjectId,
                isCreate: false,
                cancellationToken,
                notifyManager: managerChanged,
                notifyPartner: partnerChanged);
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<Project>> PostProject(Project project, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Create))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        var validationError = await ValidateProjectWriteAsync(project, firmId.Value);
        if (!string.IsNullOrEmpty(validationError))
            return BadRequest(validationError);

        var newTypeId = NormalizeOptionalProjectTypeId(project.ProjectTypeId);
        var wantPartner = NormalizeOptionalPartnerInChargeId(project.PartnerInChargeId);

        project.ProjectId = 0;
        project.FirmId = firmId.Value;
        project.ProjectCode = project.ProjectCode.Trim();
        project.Name = project.Name.Trim();
        project.Status = project.Status?.Trim() ?? "Active";
        project.ProjectTypeId = newTypeId;
        project.PartnerInChargeId = wantPartner;
        project.DefaultReminderDaysBeforeDue = NormalizeDefaultReminderDays(project.DefaultReminderDaysBeforeDue);
        project.CreatedAt = DateTime.UtcNow;

        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);
        await TrySendProjectSavedEmailAsync(
            project.ProjectId,
            isCreate: true,
            cancellationToken,
            notifyManager: true,
            notifyPartner: true);
        return CreatedAtAction(nameof(GetProject), new { id = project.ProjectId }, project);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Projects,
                ResourcePermissionAction.Delete))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectId == id && p.FirmId == firmId);
        if (project == null) return NotFound();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task TrySendProjectSavedEmailAsync(
        int projectId,
        bool isCreate,
        CancellationToken cancellationToken,
        bool notifyManager = true,
        bool notifyPartner = true)
    {
        var data = await LoadProjectEmailDataAsync(projectId, cancellationToken);
        if (data == null)
            return;

        var recipients = new List<ProjectEmailRecipient>();
        if (notifyManager && !string.IsNullOrWhiteSpace(data.ManagerEmail))
            recipients.Add(new ProjectEmailRecipient(data.ManagerName, data.ManagerEmail, "Manager"));
        if (notifyPartner && !string.IsNullOrWhiteSpace(data.PartnerEmail))
            recipients.Add(new ProjectEmailRecipient(data.PartnerName, data.PartnerEmail, "Partner"));

        if (recipients.Count == 0)
            return;

        var link = ProjectsLink();
        var safeLink = link == "#" ? null : WebUtility.HtmlEncode(link);
        var action = isCreate ? "creado" : "guardado";
        var projectEsc = WebUtility.HtmlEncode(data.ProjectName);
        var codeEsc = WebUtility.HtmlEncode(data.ProjectCode);
        var customerEsc = WebUtility.HtmlEncode(data.CustomerName);
        var typeEsc = WebUtility.HtmlEncode(data.ProjectTypeName);
        var statusEsc = WebUtility.HtmlEncode(data.Status);
        var auditPeriodEsc = WebUtility.HtmlEncode(data.AuditPeriod.ToString());
        var budgetEsc = WebUtility.HtmlEncode(data.BudgetedHours.ToString("0.##"));
        var startDateEsc = WebUtility.HtmlEncode(FormatDate(data.StartDate));

        foreach (var recipient in recipients
            .GroupBy(r => r.Email.Trim().ToUpperInvariant())
            .Select(g => new ProjectEmailRecipient(
                g.First().Name,
                g.First().Email,
                string.Join(" / ", g.Select(r => r.Role).Distinct()))))
        {
            var roleEsc = WebUtility.HtmlEncode(recipient.Role);
            var highlight =
                $"""
                <div style="font-size:20px;font-weight:700;line-height:1.35;color:{AmperaEmailPalette.TealOscuro};">{projectEsc}</div>
                <div style="margin-top:12px;font-size:14px;line-height:1.65;color:{AmperaEmailPalette.TextoSecundario};">
                  <strong>Código:</strong> {codeEsc}<br />
                  <strong>Cliente:</strong> {customerEsc}<br />
                  <strong>Tipo de proyecto:</strong> {typeEsc}<br />
                  <strong>Estado:</strong> {statusEsc}<br />
                  <strong>Año fiscal:</strong> {auditPeriodEsc}<br />
                  <strong>Rol asignado:</strong> {roleEsc}<br />
                  <strong>Horas presupuestadas:</strong> {budgetEsc}<br />
                  <strong>Fecha inicio:</strong> {startDateEsc}
                </div>
                """;

            var intro = AmperaBrandedEmail.BodyParagraph(
                $"Se ha <strong>{WebUtility.HtmlEncode(action)}</strong> el proyecto <strong>{projectEsc}</strong> " +
                $"y usted figura como <strong>{roleEsc}</strong>. Debajo encontrará los datos obligatorios del proyecto.");

            var html = AmperaBrandedEmail.BuildDocument(
                htmlPageTitle: "Proyecto asignado — AMPERA",
                firmDisplayName: data.FirmName,
                recipientName: recipient.Name,
                introAfterGreetingHtml: intro,
                highlightLabel: "Proyecto",
                highlightInnerHtml: highlight,
                primaryButtonHref: link == "#" ? null : link,
                primaryButtonText: link == "#" ? null : "Ver proyectos",
                fallbackLinkDisplayEncoded: safeLink);

            try
            {
                await _emailService.SendHtmlAsync(
                    new[] { recipient.Email },
                    $"AMPERA — Proyecto asignado {data.ProjectName}",
                    html,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo enviar el correo del proyecto {ProjectId} al usuario {Email}.",
                    projectId,
                    recipient.Email);
            }
        }
    }

    private async Task<ProjectEmailData> LoadProjectEmailDataAsync(int projectId, CancellationToken cancellationToken)
    {
        var data = await _context.Projects
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Select(p => new ProjectEmailData(
                p.ProjectCode ?? "",
                p.Name ?? "",
                p.Status ?? "",
                p.AuditPeriod,
                p.BudgetedHours,
                p.StartDate,
                p.Firm.Name ?? "AMPERA",
                p.Customer.Name ?? "",
                p.ProjectType != null ? p.ProjectType.Name : "",
                p.Manager.FullName ?? "",
                p.Manager.Email ?? "",
                p.PartnerInCharge != null ? p.PartnerInCharge.FullName : "",
                p.PartnerInCharge != null ? p.PartnerInCharge.Email : ""))
            .FirstOrDefaultAsync(cancellationToken);

        return data;
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "—";
    }

    private string ProjectsLink()
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? "#"
            : $"{baseUrl}/projects";
    }

    private sealed record ProjectEmailRecipient(string Name, string Email, string Role);

    private sealed record ProjectEmailData(
        string ProjectCode,
        string ProjectName,
        string Status,
        int AuditPeriod,
        decimal BudgetedHours,
        DateTime? StartDate,
        string FirmName,
        string CustomerName,
        string ProjectTypeName,
        string ManagerName,
        string ManagerEmail,
        string PartnerName,
        string PartnerEmail);
}
