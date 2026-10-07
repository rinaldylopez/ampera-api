using System.ComponentModel.DataAnnotations;
using System.Net;
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

namespace AuditWorkflow.Controllers.Core;

/// <summary>
/// Solicitudes del portal cliente para ampliar plazo / pedir modificación;
/// la firma aprueba o rechaza (y aplica due date si corresponde).
/// </summary>
[Authorize]
[Route("api/requests/{requestId:int}/change-requests")]
[ApiController]
public class RequestChangeRequestsController(
    AppDbContext context,
    IProfileResourcePermissionService resourcePermissions,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<RequestChangeRequestsController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = resourcePermissions;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<RequestChangeRequestsController> _logger = logger;

    public class CreateChangeRequestInput
    {
        /// <summary><see cref="RequestChangeRequestKinds"/>.</summary>
        [Range(1, 3)]
        public int Kind { get; set; }

        public DateTime? ProposedDueDate { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Reason { get; set; } = "";
    }

    public class ReviewChangeRequestInput
    {
        [MaxLength(2000)]
        public string? ReviewNote { get; set; }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RequestChangeRequest>>> List(
        int requestId,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        return await _context.RequestChangeRequests
            .AsNoTracking()
            .Include(c => c.RequestedBy)
            .Include(c => c.ReviewedBy)
            .Where(c => c.RequestId == requestId)
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    [HttpGet("pending")]
    public async Task<ActionResult<RequestChangeRequest?>> GetPending(
        int requestId,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var pending = await _context.RequestChangeRequests
            .AsNoTracking()
            .Include(c => c.RequestedBy)
            .Where(c => c.RequestId == requestId && c.Status == RequestChangeRequestStatuses.Pending)
            .OrderByDescending(c => c.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(pending);
    }

    /// <summary>Portal cliente: crea una solicitud de cambio (solo una pendiente a la vez).</summary>
    [HttpPost]
    public async Task<ActionResult<RequestChangeRequest>> Create(
        int requestId,
        [FromBody] CreateChangeRequestInput input,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var reason = (input.Reason ?? "").Trim();
        if (reason.Length < 5)
            return BadRequest("Indique el motivo (mínimo 5 caracteres).");
        if (!RequestChangeRequestKinds.IsValid(input.Kind))
            return BadRequest("Tipo de solicitud inválido.");

        var entity = await _context.Requests
            .Include(r => r.Project)!.ThenInclude(p => p!.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
        if (entity == null) return NotFound();
        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");
        if (entity.Status == 0)
            return BadRequest("La solicitud aún está en borrador.");

        var hasPending = await _context.RequestChangeRequests.AnyAsync(
            c => c.RequestId == requestId && c.Status == RequestChangeRequestStatuses.Pending,
            cancellationToken);
        if (hasPending)
            return BadRequest("Ya hay una solicitud de cambio pendiente. Espere la respuesta de la firma o cancélela.");

        DateTime? proposed = null;
        if (RequestChangeRequestKinds.RequiresProposedDueDate(input.Kind))
        {
            if (input.ProposedDueDate is not DateTime pd)
                return BadRequest("Indique la nueva fecha de vencimiento propuesta.");
            proposed = pd.Date;
            var today = ServerClock.Now.Date;
            if (proposed.Value < today)
                return BadRequest("La fecha propuesta no puede ser anterior a hoy.");
        }

        var row = new RequestChangeRequest
        {
            RequestId = requestId,
            RequestedByUserId = userId.Value,
            RequestedAt = ServerClock.Now,
            Kind = input.Kind,
            ProposedDueDate = proposed,
            Reason = reason,
            Status = RequestChangeRequestStatuses.Pending
        };
        _context.RequestChangeRequests.Add(row);

        var kindLabel = KindLabelEs(input.Kind);
        var duePart = proposed is DateTime d
            ? $" Nueva fecha propuesta: {d:dd/MM/yyyy}."
            : "";
        RequestHistoryLog.Add(
            _context,
            requestId,
            userId.Value,
            RequestHistoryLog.ChangeRequest,
            $"El cliente solicitó {kindLabel}.{duePart} Motivo: {reason}");

        await _context.SaveChangesAsync(cancellationToken);

        await NotifyFirmPendingAsync(entity, row, cancellationToken);

        var created = await _context.RequestChangeRequests.AsNoTracking()
            .Include(c => c.RequestedBy)
            .FirstAsync(c => c.RequestChangeRequestId == row.RequestChangeRequestId, cancellationToken);

        return CreatedAtAction(nameof(GetPending), new { requestId }, created);
    }

    /// <summary>Portal cliente: cancela su solicitud pendiente.</summary>
    [HttpPost("{changeRequestId:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int requestId,
        int changeRequestId,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(
                _context, requestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var row = await _context.RequestChangeRequests
            .FirstOrDefaultAsync(
                c => c.RequestChangeRequestId == changeRequestId && c.RequestId == requestId,
                cancellationToken);
        if (row == null) return NotFound();
        if (row.Status != RequestChangeRequestStatuses.Pending)
            return BadRequest("Solo se puede cancelar una solicitud pendiente.");
        if (row.RequestedByUserId != userId.Value)
            return Forbid();

        row.Status = RequestChangeRequestStatuses.CancelledByClient;
        row.ReviewedAt = ServerClock.Now;
        row.ReviewedByUserId = userId.Value;
        row.ReviewNote = "Cancelada por el cliente.";

        RequestHistoryLog.Add(
            _context,
            requestId,
            userId.Value,
            RequestHistoryLog.ChangeRequest,
            "El cliente canceló su solicitud de cambio.");

        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Firma: aprueba; si hay fecha propuesta, la aplica al requerimiento.</summary>
    [HttpPost("{changeRequestId:int}/approve")]
    public async Task<IActionResult> Approve(
        int requestId,
        int changeRequestId,
        [FromBody] ReviewChangeRequestInput? input,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(
                User, ProfileProtectedResource.Requests, ResourcePermissionAction.Update, cancellationToken))
            return Forbid();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))
            return NotFound();

        var row = await _context.RequestChangeRequests
            .Include(c => c.RequestedBy)
            .FirstOrDefaultAsync(
                c => c.RequestChangeRequestId == changeRequestId && c.RequestId == requestId,
                cancellationToken);
        if (row == null) return NotFound();
        if (row.Status != RequestChangeRequestStatuses.Pending)
            return BadRequest("La solicitud de cambio ya fue resuelta.");

        var request = await _context.Requests
            .Include(r => r.Project)!.ThenInclude(p => p!.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
        if (request == null) return NotFound();
        if (request.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        row.PreviousDueDate = request.DueDate;
        row.Status = RequestChangeRequestStatuses.Approved;
        row.ReviewedByUserId = userId.Value;
        row.ReviewedAt = ServerClock.Now;
        row.ReviewNote = string.IsNullOrWhiteSpace(input?.ReviewNote) ? null : input!.ReviewNote.Trim();

        var appliedDue = false;
        if (row.ProposedDueDate is DateTime newDue &&
            RequestChangeRequestKinds.RequiresProposedDueDate(row.Kind))
        {
            request.DueDate = newDue.Date;
            appliedDue = true;
        }

        var notePart = string.IsNullOrWhiteSpace(row.ReviewNote) ? "" : $" Nota: {row.ReviewNote}";
        var duePart = appliedDue
            ? $" Nueva fecha de vencimiento: {request.DueDate:dd/MM/yyyy}."
            : "";
        RequestHistoryLog.Add(
            _context,
            requestId,
            userId.Value,
            RequestHistoryLog.ChangeRequest,
            $"La firma aprobó la solicitud de cambio del cliente.{duePart}{notePart}");

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyClientResolvedAsync(request, row, approved: true, cancellationToken);
        return NoContent();
    }

    /// <summary>Firma: rechaza la solicitud de cambio.</summary>
    [HttpPost("{changeRequestId:int}/reject")]
    public async Task<IActionResult> Reject(
        int requestId,
        int changeRequestId,
        [FromBody] ReviewChangeRequestInput? input,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await _resourcePermissions.AllowsFirmMutationAsync(
                User, ProfileProtectedResource.Requests, ResourcePermissionAction.Update, cancellationToken))
            return Forbid();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))
            return NotFound();

        var row = await _context.RequestChangeRequests
            .Include(c => c.RequestedBy)
            .FirstOrDefaultAsync(
                c => c.RequestChangeRequestId == changeRequestId && c.RequestId == requestId,
                cancellationToken);
        if (row == null) return NotFound();
        if (row.Status != RequestChangeRequestStatuses.Pending)
            return BadRequest("La solicitud de cambio ya fue resuelta.");

        var request = await _context.Requests
            .Include(r => r.Project)!.ThenInclude(p => p!.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);
        if (request == null) return NotFound();

        var note = (input?.ReviewNote ?? "").Trim();
        if (note.Length < 3)
            return BadRequest("Indique el motivo del rechazo (mínimo 3 caracteres).");

        row.Status = RequestChangeRequestStatuses.Rejected;
        row.ReviewedByUserId = userId.Value;
        row.ReviewedAt = ServerClock.Now;
        row.ReviewNote = note;

        RequestHistoryLog.Add(
            _context,
            requestId,
            userId.Value,
            RequestHistoryLog.ChangeRequest,
            $"La firma rechazó la solicitud de cambio del cliente. Motivo: {note}");

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyClientResolvedAsync(request, row, approved: false, cancellationToken);
        return NoContent();
    }

    private async Task NotifyFirmPendingAsync(
        Request request,
        RequestChangeRequest change,
        CancellationToken cancellationToken)
    {
        var project = request.Project;
        if (project == null) return;

        var firmId = project.FirmId;
        var recipients = await RequestNotificationRecipients.GetFirmStaffUserIdsAsync(
            _context, firmId, request.RequestId, project.ProjectId, request.FirmContactId, cancellationToken);
        if (recipients.Count == 0) return;

        var title = FormatTitlesPlain(request.Title, request.TitleEn);
        var dueText = change.ProposedDueDate is DateTime d
            ? $" Nueva fecha propuesta: {d:dd/MM/yyyy}."
            : "";
        var body =
            $"{project.Customer?.Name?.Trim()} solicitó {KindLabelEs(change.Kind)} sobre «{title}».{dueText} Motivo: {change.Reason}";

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId,
            recipients,
            "Solicitud de cambio del cliente",
            body.Length <= 1000 ? body : body[..1000],
            $"/requests/{request.RequestGuid:D}",
            request.RequestId,
            NotificationKind.ClientChangeRequestPending,
            cancellationToken);

        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipients, cancellationToken);
        if (emails.Count == 0) return;

        var link = FirmRequestLink(request.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Solicitud de cambio — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: "El cliente solicita un cambio",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(WebUtility.HtmlEncode(body)),
            primaryButtonHref: link,
            primaryButtonText: "Abrir solicitud",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(link));

        try
        {
            await _emailService.SendHtmlAsync(
                emails.ToHashSet(StringComparer.OrdinalIgnoreCase),
                $"Cambio solicitado: {title}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar correo de change-request pendiente {Id}.", change.RequestChangeRequestId);
        }
    }

    private async Task NotifyClientResolvedAsync(
        Request request,
        RequestChangeRequest change,
        bool approved,
        CancellationToken cancellationToken)
    {
        var project = request.Project;
        if (project?.CustomerId is not int customerId) return;

        var recipients = await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
            _context, project.FirmId, customerId, request.RequestId, cancellationToken);
        if (recipients.Count == 0 && change.RequestedByUserId > 0)
            recipients = [change.RequestedByUserId];
        if (recipients.Count == 0) return;

        var title = FormatTitlesPlain(request.Title, request.TitleEn);
        var outcome = approved ? "aprobó" : "rechazó";
        var duePart = approved && request.DueDate is DateTime nd
            ? $" Fecha de vencimiento actual: {nd:dd/MM/yyyy}."
            : "";
        var notePart = string.IsNullOrWhiteSpace(change.ReviewNote) ? "" : $" Nota de la firma: {change.ReviewNote}";
        var body = $"La firma {outcome} su solicitud de cambio sobre «{title}».{duePart}{notePart}";

        await UserNotificationCommands.CreateManyAsync(
            _context,
            project.FirmId,
            recipients,
            approved ? "Cambio aprobado" : "Cambio rechazado",
            body.Length <= 1000 ? body : body[..1000],
            $"/portal/requests/{request.RequestGuid:D}",
            request.RequestId,
            NotificationKind.ClientChangeRequestResolved,
            cancellationToken);

        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipients, cancellationToken);
        if (emails.Count == 0) return;

        var link = ClientRequestLink(request.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Respuesta a su solicitud — AMPERA",
            headerSubtitle: "Portal del cliente",
            cardHeadline: approved ? "Su solicitud de cambio fue aprobada" : "Su solicitud de cambio fue rechazada",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(WebUtility.HtmlEncode(body)),
            primaryButtonHref: link,
            primaryButtonText: "Abrir solicitud",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(link));

        try
        {
            await _emailService.SendHtmlAsync(
                emails.ToHashSet(StringComparer.OrdinalIgnoreCase),
                approved ? $"Cambio aprobado: {title}" : $"Cambio rechazado: {title}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar correo de change-request resuelto {Id}.", change.RequestChangeRequestId);
        }
    }

    private static string KindLabelEs(int kind) => kind switch
    {
        RequestChangeRequestKinds.DueDateExtension => "ampliación de plazo",
        RequestChangeRequestKinds.ContentModification => "modificación del requerimiento",
        RequestChangeRequestKinds.Both => "ampliación de plazo y modificación",
        _ => "un cambio"
    };

    private static string FormatTitlesPlain(string? title, string? titleEn)
    {
        var es = (title ?? "").Trim();
        var en = (titleEn ?? "").Trim();
        if (string.IsNullOrEmpty(es)) return en;
        if (string.IsNullOrEmpty(en)) return es;
        return $"{es} / {en}";
    }

    private string FirmRequestLink(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/requests/{requestGuid:D}";
    }

    private string ClientRequestLink(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/portal/requests/{requestGuid:D}";
    }
}
