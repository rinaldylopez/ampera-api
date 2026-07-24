using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net;
using System.Text;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class RequestsController(
    AppDbContext context,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<RequestsController> logger,
    IProfileResourcePermissionService resourcePermissions) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly IProfileResourcePermissionService _resourcePermissions = resourcePermissions;

    private const string RequestExcelExportMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string ForbiddenResourceMessage = "No tiene permiso para esta operación.";

    private const string RequestDeleteBlockedMessage =
        "No se puede eliminar esta solicitud porque ya tiene información asociada " +
        "(comentarios del expediente, documentación aportada por el cliente o archivos compartidos por la firma). " +
        "Abra el detalle de la solicitud, elimine o gestione ese contenido y vuelva a intentarlo.";

    private const string RequestDeleteNotDraftMessage =
        "No se puede eliminar una solicitud que ya fue enviada al cliente o está en trámite en el portal.";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Request>>> GetRequests(
        [FromQuery] int? projectId,
        [FromQuery] int? customerId,
        [FromQuery] string? publishQueue)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        var q = _context.Requests
            .AsNoTracking()
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .Include(r => r.FirmContact)
            .Where(r => r.Project.FirmId == firmId);

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
        {
            var cc = User.GetCustomerId();
            if (cc is null)
                return Unauthorized();
            q = q.Where(r => r.Project.CustomerId == cc.Value);
            q = q.Where(r => r.Status != 0);
        }
        else
        {
            if (customerId is int cid && !await TenantQueries.CustomerBelongsToFirmAsync(_context, cid, firmId.Value))
                return BadRequest("Cliente inválido.");
            if (customerId is int c)
                q = q.Where(r => r.Project.CustomerId == c);
        }

        if (projectId is int pidFilter)
            q = q.Where(r => r.ProjectId == pidFilter);

        if (!string.IsNullOrWhiteSpace(publishQueue))
        {
            if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
                return BadRequest("Filtros de cola de publicación no aplican en el portal cliente.");
            publishQueue = publishQueue.Trim().ToLowerInvariant();
            if (publishQueue == "staff")
            {
                q = q.Where(r =>
                    r.Status == 0 &&
                    r.PublishToClientApprovalState == RequestPublishToClientApproval.None &&
                    !(r.Project.ManagerId == userId.Value || r.Project.PartnerInChargeId == userId.Value));
            }
            else if (publishQueue == "approver")
            {
                q = q.Where(r =>
                    r.Status == 0 &&
                    r.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner &&
                    (r.Project.ManagerId == userId.Value || r.Project.PartnerInChargeId == userId.Value));
            }
            else if (publishQueue == "manager-draft")
            {
                q = q.Where(r =>
                    r.Status == 0 &&
                    (r.Project.ManagerId == userId.Value || r.Project.PartnerInChargeId == userId.Value));
            }
            else if (publishQueue == "pending-own")
            {
                q = q.Where(r =>
                    r.Status == 0 &&
                    r.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner &&
                    r.PublishApprovalRequestedByUserId == userId.Value);
            }
            else if (publishQueue == "rejected-own")
            {
                q = q.Where(r =>
                    r.Status == 0 &&
                    r.PublishToClientApprovalState == RequestPublishToClientApproval.RejectedByManagerPartner &&
                    r.PublishApprovalRequestedByUserId == userId.Value);
            }
            else
                return BadRequest(
                    "publishQueue inválido (use «staff», «approver», «manager-draft», «pending-own» o «rejected-own»).");
        }

        var list = projectId is int
            ? await q.OrderByDescending(r => r.RequestNumber).ThenByDescending(r => r.RequestId).ToListAsync()
            : await q.OrderByDescending(r => r.CreatedAt).ToListAsync();

        await EnrichPublishRejectionSummariesAsync(list);
        await EnrichPublishedToClientDatesAsync(list);
        return list;
    }

    /// <summary>Exporta Excel con mismos filtros base que <see cref="GetRequests"/> (portal y firma).</summary>
    [HttpGet("export-excel")]
    public async Task<IActionResult> ExportRequestsExcel([FromQuery] int? projectId, [FromQuery] int? customerId)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null)
            return Unauthorized();

        var q = _context.Requests
            .AsNoTracking()
            .Where(r => r.Project.FirmId == firmId.Value);

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
        {
            var cc = User.GetCustomerId();
            if (cc is null)
                return Unauthorized();
            q = q.Where(r => r.Project.CustomerId == cc.Value);
            q = q.Where(r => r.Status != 0);
        }
        else
        {
            if (customerId is int cid && !await TenantQueries.CustomerBelongsToFirmAsync(_context, cid, firmId.Value))
                return BadRequest("Cliente inválido.");
            if (customerId is int c)
                q = q.Where(r => r.Project.CustomerId == c);
        }

        if (projectId is int pidFilter)
            q = q.Where(r => r.ProjectId == pidFilter);

        var rows = await q.OrderByDescending(r => r.RequestNumber).ThenByDescending(r => r.RequestId).Select(r => new
        {
            r.RequestNumber,
            r.Title,
            r.TitleEn,
            ProjCode = r.Project.ProjectCode ?? "",
            ProjName = r.Project.Name ?? "",
            r.Status,
            r.Priority,
            r.DueDate,
            r.CreatedAt
        }).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Solicitudes");
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Nombre (ES)";
        ws.Cell(1, 3).Value = "Nombre (EN)";
        ws.Cell(1, 4).Value = "Proyecto";
        ws.Cell(1, 5).Value = "Estado";
        ws.Cell(1, 6).Value = "Prioridad";
        ws.Cell(1, 7).Value = "Vencimiento";
        ws.Cell(1, 8).Value = "Creado (UTC)";
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        var rowIx = 2;
        foreach (var x in rows)
        {
            ws.Cell(rowIx, 1).Value = x.RequestNumber;
            ws.Cell(rowIx, 2).Value = x.Title ?? "";
            ws.Cell(rowIx, 3).Value = x.TitleEn ?? "";
            var pcode = string.IsNullOrWhiteSpace(x.ProjCode) ? "" : x.ProjCode.Trim();
            var pname = string.IsNullOrWhiteSpace(x.ProjName) ? "" : x.ProjName.Trim();
            ws.Cell(rowIx, 4).Value = pcode.Length > 0
                ? (pname.Length > 0 ? $"{pcode} — {pname}" : pcode)
                : pname;
            ws.Cell(rowIx, 5).Value = RequestExportStatusEs(x.Status);
            ws.Cell(rowIx, 6).Value = RequestExportPriorityEs(x.Priority);
            if (x.DueDate.HasValue)
            {
                var d = x.DueDate.Value;
                ws.Cell(rowIx, 7).Value = new DateTime(d.Year, d.Month, d.Day);
                ws.Cell(rowIx, 7).Style.DateFormat.Format = "yyyy-mm-dd";
            }
            else
            {
                ws.Cell(rowIx, 7).Clear();
            }

            ws.Cell(rowIx, 8).Value = x.CreatedAt;
            ws.Cell(rowIx, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            rowIx++;
        }

        ws.Columns().AdjustToContents(1, 200);

        string fileSlug;
        if (projectId is int pjForName)
        {
            var slug = await _context.Projects.AsNoTracking()
                .Where(p => p.ProjectId == pjForName && p.FirmId == firmId.Value)
                .Select(p => p.ProjectCode ?? p.Name ?? "proyecto")
                .FirstOrDefaultAsync();
            fileSlug = SanitizeExcelFileSlug(slug ?? "proyecto");
        }
        else
        {
            fileSlug = "todas";
        }

        await using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"solicitudes-{fileSlug}-{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(), RequestExcelExportMime, fileName);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Request>> GetRequest(int id)
    {
        return await GetRequestByPredicate(id, q => q.Where(r => r.RequestId == id));
    }

    [HttpGet("by-guid/{requestGuid:guid}")]
    public async Task<ActionResult<Request>> GetRequestByGuid(Guid requestGuid)
    {
        return await GetRequestByPredicate(null, q => q.Where(r => r.RequestGuid == requestGuid));
    }

    private async Task<ActionResult<Request>> GetRequestByPredicate(
        int? requestId,
        Func<IQueryable<Request>, IQueryable<Request>> filter)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        if (requestId is int id && !await TenantQueries.RequestAccessibleToUserAsync(_context, id, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var query = _context.Requests
            .AsNoTracking()
            .Include(r => r.Project)!
                .ThenInclude(p => p.ProjectTeams)!
                    .ThenInclude(pt => pt.Team)
                        .ThenInclude(t => t.TeamMembers)!
                            .ThenInclude(tm => tm.User)
            .Include(r => r.Project)!
                .ThenInclude(p => p.ProjectStaffs)!
                    .ThenInclude(ps => ps.User)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Manager)
            .Include(r => r.Project)!
                .ThenInclude(p => p.PartnerInCharge)
            .Include(r => r.FirmContact)
            .Include(r => r.PublishApprovalRequestedBy)
            .Include(r => r.ClosedBy)
            .Include(r => r.Assignments)!.ThenInclude(a => a.Team)
            .Include(r => r.Assignments)!.ThenInclude(a => a.User)
            .Include(r => r.Attachments)
            .Include(r => r.Comments)!.ThenInclude(c => c.User);

        var request = await filter(query).FirstOrDefaultAsync();

        if (request != null && requestId == null && !await TenantQueries.RequestAccessibleToUserAsync(_context, request.RequestId, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        if (request == null) return NotFound();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value) && request.Status == 0)
            return NotFound();

        await EnrichPublishRejectionSummariesAsync([request]);
        await EnrichPublishedToClientDatesAsync([request]);
        return request;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutRequest(int id, Request request, CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidPut)
            return forbidPut;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (id != request.RequestId) return BadRequest();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();
        var existing = await _context.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == id);
        if (existing == null) return NotFound();
        if (!ValidateRequestCore(request, out var err))
            return BadRequest(err);
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, request.ProjectId, firmId.Value))
            return BadRequest();
        if (!await TenantQueries.UserIsActiveFirmStaffInFirmAsync(_context, request.FirmContactId, firmId.Value))
            return BadRequest("El contacto de firma debe ser un usuario interno activo de su firma.");

        if (existing.ClosedAt != null)
        {
            if (request.Status != 4)
                return BadRequest("La solicitud está cerrada. El estado debe ser «Recibida / cerrada». Use Reabrir en el detalle si debe reactivar el flujo.");
        }
        else if (request.Status == 4)
        {
            return BadRequest("Para marcar la solicitud como recibida y cerrada use la acción «Cerrar solicitud» en el detalle, no el formulario de edición.");
        }

        if (existing.Status == 0 && request.Status != 0)
            return BadRequest("Para publicar la solicitud en el portal del cliente use «Enviar al cliente» en el detalle.");

        if (request.ProjectId != existing.ProjectId)
            return BadRequest("No se puede cambiar el proyecto de una solicitud ya creada.");

        if (request.Status == 5 && existing.PortalHandoffStatus != (int)RequestPortalHandoff.WithFirm)
            return BadRequest("El estado «En revisión por la firma» solo aplica cuando la documentación está con la firma.");

        request.PortalHandoffStatus = existing.PortalHandoffStatus;
        request.RequestGuid = existing.RequestGuid;
        request.RequestNumber = existing.RequestNumber;
        request.ClosedAt = existing.ClosedAt;
        request.ClosedById = existing.ClosedById;
        request.PublishToClientApprovalState = existing.PublishToClientApprovalState;
        request.PublishApprovalRequestedUtc = existing.PublishApprovalRequestedUtc;
        request.PublishApprovalRequestedByUserId = existing.PublishApprovalRequestedByUserId;

        if (existing.PortalHandoffStatus == (int)RequestPortalHandoff.WithFirm && existing.ClosedAt == null)
            request.Status = 5;

        request.Project = null;
        request.FirmContact = null;
        request.ClosedBy = null;
        request.Assignments = null;
        request.Attachments = null;
        request.Comments = null;
        request.PublishApprovalRequestedBy = null;
        _context.Entry(request).State = EntityState.Modified;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Updated,
            $"La firma actualizó la solicitud \"{request.Title}\" · EN: \"{request.TitleEn}\".");
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Requests.AnyAsync(r => r.RequestId == id))
                return NotFound();
            throw;
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> PostRequest(Request request, CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Create, cancellationToken) is
            IActionResult forbidPost)
            return forbidPost;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        if (!ValidateRequestCore(request, out var err))
            return BadRequest(err);
        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, request.ProjectId, firmId.Value))
            return BadRequest("Proyecto inválido.");
        if (!await TenantQueries.UserIsActiveFirmStaffInFirmAsync(_context, request.FirmContactId, firmId.Value))
            return BadRequest("El contacto de firma debe ser un usuario interno activo de su firma.");

        request.Status = 0;

        request.UserCreatedId = userId.Value;
        if (request.RequestGuid == Guid.Empty)
            request.RequestGuid = Guid.NewGuid();
        request.CreatedAt = DateTime.UtcNow;
        request.PortalHandoffStatus = (int)RequestPortalHandoff.WithClient;
        request.Project = null;
        request.FirmContact = null;
        request.Assignments = null;
        request.Attachments = null;
        request.Comments = null;
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            request.RequestNumber = await RequestNumberAllocator.AllocateNextAsync(
                _context, request.ProjectId, cancellationToken);
            _context.Requests.Add(request);
            await _context.SaveChangesAsync(cancellationToken);
            RequestHistoryLog.Add(_context, request.RequestId, userId.Value, RequestHistoryLog.Created,
                $"La firma creó la solicitud \"{request.Title}\" · EN: \"{request.TitleEn}\".");
            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        return CreatedAtAction(nameof(GetRequestByGuid), new { requestGuid = request.RequestGuid }, request);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRequest(int id, CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Delete, cancellationToken) is
            IActionResult forbidDel)
            return forbidDel;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var err = await TryDeleteRequestAsync(id, firmId.Value, userId.Value, cancellationToken);
        if (err.Error == "Solicitud no encontrada.")
            return NotFound();
        if (err.Error != null)
            return BadRequest(err.Error);

        if (err.ProjectId is int projectId)
            await RequestNumberRenumberer.RenumberDraftsInProjectAsync(_context, projectId, cancellationToken);

        return NoContent();
    }

    [HttpPost("batch/delete")]
    public async Task<IActionResult> BatchDeleteRequests(
        [FromBody] RequestIdBatchDto? body,
        CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Delete, cancellationToken) is
            IActionResult forbidDel)
            return forbidDel;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();

        var ids = (body?.RequestIds ?? [])
            .Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("Indique al menos una solicitud.");

        var result = new RequestBatchResultDto();
        var projectsToRenumber = new HashSet<int>();

        foreach (var id in ids)
        {
            try
            {
                var del = await TryDeleteRequestAsync(id, firmId.Value, userId.Value, cancellationToken);
                if (del.Error == null)
                {
                    result.Succeeded.Add(id);
                    if (del.ProjectId is int pid)
                        projectsToRenumber.Add(pid);
                }
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = del.Error });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo eliminación masiva solicitud id {RequestId}", id);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = ex.Message });
            }
        }

        foreach (var projectId in projectsToRenumber)
            await RequestNumberRenumberer.RenumberDraftsInProjectAsync(_context, projectId, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Publicar en portal cliente desde borrador. Si quien ejecuta no es manager ni socio, queda pendiente la aprobación;
    /// el manager/socio ejecuta esta misma acción para enviar efectivamente tras aprobar.
    /// </summary>
    [HttpPost("{id:int}/publish-to-client")]
    public async Task<IActionResult> PublishToClient(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidPub)
            return forbidPub;
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Manager)
            .Include(r => r.Project)!
                .ThenInclude(p => p.PartnerInCharge)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity == null) return NotFound();

        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        var project = entity.Project;
        if (project == null)
            return BadRequest("Proyecto de la solicitud no encontrado.");

        var awaiting = entity.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner;

        if (!awaiting)
        {
            if (entity.Status != 0)
                return BadRequest("Solo puede usar «Enviar al cliente» cuando la solicitud está en borrador.");
        }
        else
        {
            if (entity.Status != 0)
                return Conflict("Estado inconsistente: hay aprobación pendiente pero la solicitud ya no está en borrador. Contacte a soporte.");
            if (!IsProjectManagerOrPartner(project, userId.Value))
                return BadRequest(
                    "La solicitud está pendiente de aprobación por el manager o el socio responsable del encargo.");

            var customerGatePassed = entity.Project.CustomerId is int cidGatePg &&
                                      await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId.Value, cidGatePg, cancellationToken);
            if (!customerGatePassed)
                return BadRequest(
                    "No hay usuarios del cliente activos para esta empresa. Cree al menos un usuario del portal del cliente antes de enviar la solicitud.");

            var requestorId = entity.PublishApprovalRequestedByUserId;

            await ClearPublishApprovalStateAsync(entity);
            RecordPublishApprovalFlowEvent(
                entity,
                RequestPublishApprovalEventTypes.ApprovedAndPublished,
                userId.Value,
                requestorId);

            entity.Status = 2;
            await _context.SaveChangesAsync(cancellationToken);
            await SendPublishedToClientNotificationsAsync(entity, firmId.Value, cancellationToken);
            return NoContent();
        }

        if (entity.Project.CustomerId is int cidGate &&
            !await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId.Value, cidGate, cancellationToken))
            return BadRequest(
                "No hay usuarios del cliente activos para esta empresa. Cree al menos un usuario del portal del cliente antes de enviar la solicitud.");

        if (IsProjectManagerOrPartner(project, userId.Value))
        {
            RecordPublishApprovalFlowEvent(
                entity,
                RequestPublishApprovalEventTypes.PublishedDirect,
                userId.Value);

            entity.Status = 2;
            await ClearPublishApprovalStateAsync(entity);
            await _context.SaveChangesAsync(cancellationToken);

            await SendPublishedToClientNotificationsAsync(entity, firmId.Value, cancellationToken);
            return NoContent();
        }

        if (entity.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner)
            return Conflict("Ya existe una solicitud de envío pendiente de aprobación.");

        entity.PublishToClientApprovalState = RequestPublishToClientApproval.PendingManagerPartner;
        entity.PublishApprovalRequestedUtc = DateTime.UtcNow;
        entity.PublishApprovalRequestedByUserId = userId.Value;

        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.ApprovalRequested,
            userId.Value);

        await _context.SaveChangesAsync(cancellationToken);

        await NotifyManagersPublishApprovalPendingAsync(entity, firmId.Value, cancellationToken);

        return Ok(new { approvalPending = true });
    }

    /// <summary>El solicitante o el manager/socio cancela el envío pendiente de aprobación (siguiendo en borrador).</summary>
    [HttpPost("{id:int}/cancel-publish-approval")]
    public async Task<IActionResult> CancelPublishApproval(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidCan)
            return forbidCan;
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity?.Project == null) return NotFound();

        if (entity.PublishToClientApprovalState != RequestPublishToClientApproval.PendingManagerPartner)
            return BadRequest("No hay envío pendiente de aprobación.");

        var canCancel =
            entity.PublishApprovalRequestedByUserId == userId.Value ||
            IsProjectManagerOrPartner(entity.Project, userId.Value);
        if (!canCancel)
            return BadRequest("Solo quien inició la solicitud de envío o el manager/socio pueden cancelar.");

        var requestorId = entity.PublishApprovalRequestedByUserId;
        await ClearPublishApprovalStateAsync(entity);
        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.Cancelled,
            userId.Value,
            requestorId);

        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>El manager/socio rechaza el envío (no publica); la solicitud queda en borrador.</summary>
    [HttpPost("{id:int}/reject-publish-approval")]
    public async Task<IActionResult> RejectPublishApproval(
        int id,
        [FromBody] RejectPublishApprovalDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidRej)
            return forbidRej;
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var reasonErr = ValidatePublishApprovalRejectReason(body?.Reason);
        if (reasonErr != null)
            return BadRequest(reasonErr);

        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity?.Project == null) return NotFound();

        if (entity.PublishToClientApprovalState != RequestPublishToClientApproval.PendingManagerPartner)
            return BadRequest("No hay envío pendiente de aprobación.");

        if (!IsProjectManagerOrPartner(entity.Project, userId.Value))
            return BadRequest("Solo el manager o el socio pueden rechazar esta solicitud de envío.");

        var requestorId = entity.PublishApprovalRequestedByUserId;
        var reason = body!.Reason.Trim();

        await SetPublishApprovalRejectedStateAsync(entity);
        RecordPublishApprovalRejection(entity, userId.Value, requestorId, reason);

        await _context.SaveChangesAsync(cancellationToken);

        if (requestorId is int rid && rid > 0 && rid != userId.Value)
            await NotifyPublishApprovalRejectedToRequestorAsync(entity, firmId.Value, rid, reason, cancellationToken);

        return NoContent();
    }

    [HttpPost("batch/request-client-publish")]
    public async Task<IActionResult> BatchRequestClientPublish(
        [FromBody] RequestIdBatchDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidBat1)
            return forbidBat1;
        var ids = (body?.RequestIds ?? [])
            .Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("Indique al menos una solicitud.");

        var result = new RequestBatchResultDto();

        foreach (var id in ids)
        {
            try
            {
                var err = await TrySingleStaffRequestPublishApprovalAsync(id, firmId.Value, userId.Value, cancellationToken,
                    deferManagerApprovalEmails: true);
                if (err == null)
                    result.Succeeded.Add(id);
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = err });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo solicitud masiva approval id {RequestId}", id);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = ex.Message });
            }
        }

        if (result.Succeeded.Count > 0)
            await SendAggregatedManagersPublishApprovalPendingEmailsAsync(firmId.Value, result.Succeeded, cancellationToken);

        return Ok(result);
    }

    [HttpPost("batch/approve-publish-to-client")]
    public async Task<IActionResult> BatchApprovePublishToClient(
        [FromBody] RequestIdBatchDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidBat2)
            return forbidBat2;
        var ids = (body?.RequestIds ?? [])
            .Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("Indique al menos una solicitud.");

        var result = new RequestBatchResultDto();

        foreach (var id in ids)
        {
            try
            {
                var err = await TrySingleManagerApprovePublishAsync(id, firmId.Value, userId.Value, cancellationToken,
                    deferClientPortalPublishEmails: true);
                if (err == null)
                    result.Succeeded.Add(id);
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = err });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo masivo publish approver id {RequestId}", id);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = ex.Message });
            }
        }

        if (result.Succeeded.Count > 0)
            await SendAggregatedClientPortalPublishedEmailsAsync(firmId.Value, result.Succeeded, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Manager/socio publica borradores al portal cliente masivamente:
    /// — si está pendiente aprobación, equivale a aprobar y publicar; — si no hay pendiente, publicación directa (misma regla que POST publish-to-client).
    /// </summary>
    [HttpPost("batch/publish-to-client")]
    public async Task<IActionResult> BatchPublishToClientForManager(
        [FromBody] RequestIdBatchDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidBat3)
            return forbidBat3;
        var ids = (body?.RequestIds ?? [])
            .Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("Indique al menos una solicitud.");

        var result = new RequestBatchResultDto();

        foreach (var id in ids)
        {
            try
            {
                var err = await TrySingleManagerBulkPublishToClientAsync(id, firmId.Value, userId.Value, cancellationToken,
                    deferClientPortalPublishEmails: true);
                if (err == null)
                    result.Succeeded.Add(id);
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = err });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo masivo publish-to-client id {RequestId}", id);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = ex.Message });
            }
        }

        if (result.Succeeded.Count > 0)
            await SendAggregatedClientPortalPublishedEmailsAsync(firmId.Value, result.Succeeded, cancellationToken);

        return Ok(result);
    }

    [HttpPost("batch/cancel-publish-approval")]
    public async Task<IActionResult> BatchCancelPublishApproval(
        [FromBody] RequestIdBatchDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidBat4)
            return forbidBat4;
        var ids = (body?.RequestIds ?? [])
            .Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest("Indique al menos una solicitud.");

        var result = new RequestBatchResultDto();

        foreach (var id in ids)
        {
            try
            {
                var err = await TrySingleCancelPublishApprovalAsync(id, firmId.Value, userId.Value, cancellationToken);
                if (err == null)
                    result.Succeeded.Add(id);
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = err });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo masivo cancel publish id {RequestId}", id);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = id, Error = ex.Message });
            }
        }

        return Ok(result);
    }

    [HttpPost("batch/reject-publish-approval")]
    public async Task<IActionResult> BatchRejectPublishApproval(
        [FromBody] RequestPublishApprovalRejectionBatchDto? body,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is
            IActionResult forbidBat5)
            return forbidBat5;

        var items = (body?.Items ?? [])
            .Where(i => i.RequestId > 0)
            .GroupBy(i => i.RequestId)
            .Select(g => g.Last())
            .ToList();
        if (items.Count == 0)
            return BadRequest("Indique al menos una solicitud con motivo de rechazo.");

        var result = new RequestBatchResultDto();

        foreach (var item in items)
        {
            try
            {
                var reasonErr = ValidatePublishApprovalRejectReason(item.Reason);
                if (reasonErr != null)
                {
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = item.RequestId, Error = reasonErr });
                    continue;
                }

                var err = await TrySingleRejectPublishApprovalAsync(
                    item.RequestId,
                    firmId.Value,
                    userId.Value,
                    item.Reason.Trim(),
                    cancellationToken);
                if (err == null)
                    result.Succeeded.Add(item.RequestId);
                else
                    result.Failed.Add(new RequestBatchFailureDto { RequestId = item.RequestId, Error = err });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo masivo reject publish id {RequestId}", item.RequestId);
                result.Failed.Add(new RequestBatchFailureDto { RequestId = item.RequestId, Error = ex.Message });
            }
        }

        return Ok(result);
    }

    private async Task<(string? Error, int? ProjectId)> TryDeleteRequestAsync(
        int id,
        int firmId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId))
            return ("No autorizado.", null);

        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return ("Solicitud no encontrada.", null);

        var request = await _context.Requests.FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (request == null)
            return ("Solicitud no encontrada.", null);

        if (request.Status != 0)
            return (RequestDeleteNotDraftMessage, null);

        var hasComments = await _context.RequestComments.AnyAsync(c => c.RequestId == id, cancellationToken);
        var hasClientAttachments = await _context.RequestAttachments.AnyAsync(a => a.RequestId == id, cancellationToken);
        var hasFirmProvidedFiles = await _context.RequestProvidedFiles.AnyAsync(a => a.RequestId == id, cancellationToken);

        if (hasComments || hasClientAttachments || hasFirmProvidedFiles)
            return (RequestDeleteBlockedMessage, null);

        var projectId = request.ProjectId;

        var assigns = await _context.RequestAssignments.Where(a => a.RequestId == id).ToListAsync(cancellationToken);
        _context.RequestAssignments.RemoveRange(assigns);
        var attachments = await _context.RequestAttachments.IgnoreQueryFilters()
            .Where(a => a.RequestId == id).ToListAsync(cancellationToken);
        _context.RequestAttachments.RemoveRange(attachments);
        var providedFiles = await _context.RequestProvidedFiles.IgnoreQueryFilters()
            .Where(a => a.RequestId == id).ToListAsync(cancellationToken);
        _context.RequestProvidedFiles.RemoveRange(providedFiles);
        var comments = await _context.RequestComments.Where(c => c.RequestId == id).ToListAsync(cancellationToken);
        _context.RequestComments.RemoveRange(comments);
        var history = await _context.RequestHistory.Where(h => h.RequestId == id).ToListAsync(cancellationToken);
        _context.RequestHistory.RemoveRange(history);

        _context.Requests.Remove(request);
        await _context.SaveChangesAsync(cancellationToken);
        return (null, projectId);
    }

    private async Task<string?> TrySingleStaffRequestPublishApprovalAsync(
        int id,
        int firmId,
        int userId,
        CancellationToken cancellationToken,
        bool deferManagerApprovalEmails = false)
    {
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return "Solicitud no encontrada.";
        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        var project = entity?.Project;
        if (project == null)
            return "Proyecto no encontrado.";

        if (entity!.ClosedAt != null)
            return "La solicitud está cerrada.";

        var awaiting = entity.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner;

        if (awaiting)
            return "Ya tiene envío pendiente de aprobación.";

        if (entity.Status != 0)
            return "La solicitud no está en borrador.";

        if (project.CustomerId is int cidGate &&
            !await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId, cidGate, cancellationToken))
            return "No hay usuarios del cliente activos para esta empresa.";

        if (IsProjectManagerOrPartner(project, userId))
            return "Como manager o socio debe usar la vista «Publicación / aprobaciones» (pestaña de publicación) para enviar borradores al cliente; esta acción es sólo para quien debe pedir esa aprobación.";

        entity.PublishToClientApprovalState = RequestPublishToClientApproval.PendingManagerPartner;
        entity.PublishApprovalRequestedUtc = DateTime.UtcNow;
        entity.PublishApprovalRequestedByUserId = userId;

        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.ApprovalRequested,
            userId);

        await _context.SaveChangesAsync(cancellationToken);
        await NotifyManagersPublishApprovalPendingAsync(entity, firmId, cancellationToken, sendSmtp: !deferManagerApprovalEmails);
        return null;
    }

    private async Task<string?> TrySingleManagerApprovePublishAsync(
        int id,
        int firmId,
        int userId,
        CancellationToken cancellationToken,
        bool deferClientPortalPublishEmails = false)
    {
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return "Solicitud no encontrada.";
        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        var project = entity?.Project;
        if (project == null)
            return "Proyecto no encontrado.";

        if (entity!.ClosedAt != null)
            return "La solicitud está cerrada.";

        if (entity.PublishToClientApprovalState != RequestPublishToClientApproval.PendingManagerPartner)
            return "No hay envío pendiente de aprobación.";

        if (entity.Status != 0)
            return "Estado inconsistente para aprobar (no está en borrador).";

        if (!IsProjectManagerOrPartner(project, userId))
            return "Solo manager o socio del proyecto pueden publicar tras la solicitud de aprobación.";

        var customerGatePassed = project.CustomerId is int cidPg &&
                                  await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId, cidPg, cancellationToken);
        if (!customerGatePassed)
            return "No hay usuarios del cliente activos para esta empresa.";

        var requestorId = entity.PublishApprovalRequestedByUserId;
        await ClearPublishApprovalStateAsync(entity);
        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.ApprovedAndPublished,
            userId,
            requestorId);

        entity.Status = 2;
        await _context.SaveChangesAsync(cancellationToken);
        await SendPublishedToClientNotificationsAsync(entity, firmId, cancellationToken, sendSmtp: !deferClientPortalPublishEmails);
        return null;
    }

    private async Task<string?> TrySingleManagerBulkPublishToClientAsync(
        int id,
        int firmId,
        int userId,
        CancellationToken cancellationToken,
        bool deferClientPortalPublishEmails = false)
    {
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return "Solicitud no encontrada.";
        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        var project = entity?.Project;
        if (project == null)
            return "Proyecto no encontrado.";

        if (entity!.ClosedAt != null)
            return "La solicitud está cerrada.";

        if (!IsProjectManagerOrPartner(project, userId))
            return "Solo el manager o el socio responsable pueden publicar al cliente desde esta cola masiva.";

        var awaiting = entity.PublishToClientApprovalState == RequestPublishToClientApproval.PendingManagerPartner;

        if (awaiting)
        {
            if (entity.Status != 0)
                return "Estado inconsistente para publicar (no está en borrador).";

            var customerGateApproved = entity.Project.CustomerId is int cidAp &&
                                       await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId, cidAp, cancellationToken);
            if (!customerGateApproved)
                return "No hay usuarios del cliente activos para esta empresa.";

            var requestorId = entity.PublishApprovalRequestedByUserId;
            await ClearPublishApprovalStateAsync(entity);
            RecordPublishApprovalFlowEvent(
                entity,
                RequestPublishApprovalEventTypes.ApprovedAndPublished,
                userId,
                requestorId);

            entity.Status = 2;
            await _context.SaveChangesAsync(cancellationToken);
            await SendPublishedToClientNotificationsAsync(entity, firmId, cancellationToken, sendSmtp: !deferClientPortalPublishEmails);
            return null;
        }

        if (entity.Status != 0)
            return "Solo pueden publicarse solicitudes que estén en borrador.";

        if (entity.Project.CustomerId is int cidGate &&
            !await TenantQueries.HasActiveClientPortalUsersAsync(_context, firmId, cidGate, cancellationToken))
            return "No hay usuarios del cliente activos para esta empresa.";

        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.PublishedDirect,
            userId);

        entity.Status = 2;
        await ClearPublishApprovalStateAsync(entity);
        await _context.SaveChangesAsync(cancellationToken);
        await SendPublishedToClientNotificationsAsync(entity, firmId, cancellationToken, sendSmtp: !deferClientPortalPublishEmails);

        return null;
    }

    private async Task<string?> TrySingleCancelPublishApprovalAsync(
        int id,
        int firmId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return "Solicitud no encontrada.";
        var entity = await _context.Requests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity?.Project == null)
            return "Solicitud no encontrada.";
        if (entity.PublishToClientApprovalState != RequestPublishToClientApproval.PendingManagerPartner)
            return "No hay envío pendiente de aprobación.";

        var canCancel =
            entity.PublishApprovalRequestedByUserId == userId ||
            IsProjectManagerOrPartner(entity.Project, userId);
        if (!canCancel)
            return "Solo quien inició la solicitud de envío o el manager/socio pueden cancelar.";

        var requestorId = entity.PublishApprovalRequestedByUserId;
        await ClearPublishApprovalStateAsync(entity);
        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.Cancelled,
            userId,
            requestorId);

        await _context.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<string?> TrySingleRejectPublishApprovalAsync(
        int id,
        int firmId,
        int userId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId))
            return "Solicitud no encontrada.";
        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity?.Project == null)
            return "Solicitud no encontrada.";

        if (entity.PublishToClientApprovalState != RequestPublishToClientApproval.PendingManagerPartner)
            return "No hay envío pendiente de aprobación.";

        if (!IsProjectManagerOrPartner(entity.Project, userId))
            return "Solo el manager o el socio pueden rechazar.";

        var requestorId = entity.PublishApprovalRequestedByUserId;

        await SetPublishApprovalRejectedStateAsync(entity);
        RecordPublishApprovalRejection(entity, userId, requestorId, reason);

        await _context.SaveChangesAsync(cancellationToken);

        if (requestorId is int rid && rid > 0 && rid != userId)
            await NotifyPublishApprovalRejectedToRequestorAsync(entity, firmId, rid, reason, cancellationToken);

        return null;
    }

    private static string? ValidatePublishApprovalRejectReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < 3)
            return "Indique el motivo del rechazo (mínimo 3 caracteres).";
        if (trimmed.Length > 2000)
            return "El motivo no puede superar 2000 caracteres.";
        return null;
    }

    private void RecordPublishApprovalFlowEvent(
        Request entity,
        int eventType,
        int actorUserId,
        int? relatedUserId = null,
        string? reason = null)
    {
        RequestPublishApprovalEventLog.Record(
            _context,
            entity.RequestId,
            eventType,
            actorUserId,
            relatedUserId,
            reason);
    }

    private void RecordPublishApprovalRejection(
        Request entity,
        int rejectedByUserId,
        int? requestedByUserId,
        string reason)
    {
        RecordPublishApprovalFlowEvent(
            entity,
            RequestPublishApprovalEventTypes.Rejected,
            rejectedByUserId,
            requestedByUserId,
            reason.Trim());
    }

    private static Task SetPublishApprovalRejectedStateAsync(Request entity)
    {
        entity.PublishToClientApprovalState = RequestPublishToClientApproval.RejectedByManagerPartner;
        entity.PublishApprovalRequestedUtc = null;
        return Task.CompletedTask;
    }

    private async Task EnrichPublishRejectionSummariesAsync(List<Request> items)
    {
        var ids = items
            .Where(r => r.PublishToClientApprovalState == RequestPublishToClientApproval.RejectedByManagerPartner)
            .Select(r => r.RequestId)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return;

        var events = await _context.RequestPublishApprovalEvents
            .AsNoTracking()
            .Include(e => e.Actor)
            .Where(e =>
                ids.Contains(e.RequestId) &&
                e.EventType == RequestPublishApprovalEventTypes.Rejected)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync();

        var latestByRequest = new Dictionary<int, RequestPublishApprovalEvent>();
        foreach (var ev in events)
        {
            if (!latestByRequest.ContainsKey(ev.RequestId))
                latestByRequest[ev.RequestId] = ev;
        }

        foreach (var row in items)
        {
            if (!latestByRequest.TryGetValue(row.RequestId, out var ev))
                continue;
            row.LastPublishRejectionReason = ev.Reason?.Trim();
            row.LastPublishRejectionAtUtc = ev.OccurredAtUtc;
            row.LastPublishRejectionActorName = ev.Actor?.FullName?.Trim();
        }
    }

    private async Task EnrichPublishedToClientDatesAsync(List<Request> items)
    {
        if (items.Count == 0)
            return;

        var ids = items.Select(r => r.RequestId).Distinct().ToList();
        var publishTypes = new[]
        {
            RequestPublishApprovalEventTypes.ApprovedAndPublished,
            RequestPublishApprovalEventTypes.PublishedDirect
        };

        var events = await _context.RequestPublishApprovalEvents
            .AsNoTracking()
            .Where(e => ids.Contains(e.RequestId) && publishTypes.Contains(e.EventType))
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync();

        var latestByRequest = new Dictionary<int, DateTime>();
        foreach (var ev in events)
        {
            if (!latestByRequest.ContainsKey(ev.RequestId))
                latestByRequest[ev.RequestId] = ev.OccurredAtUtc;
        }

        foreach (var row in items)
        {
            if (latestByRequest.TryGetValue(row.RequestId, out var at))
                row.PublishedToClientAtUtc = at;
        }
    }

    private static Task ClearPublishApprovalStateAsync(Request entity)
    {
        entity.PublishToClientApprovalState = RequestPublishToClientApproval.None;
        entity.PublishApprovalRequestedUtc = null;
        entity.PublishApprovalRequestedByUserId = null;
        return Task.CompletedTask;
    }

    private static bool IsProjectManagerOrPartner(Project project, int userId) =>
        project.ManagerId == userId ||
        (project.PartnerInChargeId is int partnerId && partnerId == userId);

    private static IEnumerable<int> ManagerPartnerRecipientIds(Project project)
    {
        if (project.ManagerId <= 0)
            yield break;

        yield return project.ManagerId;

        if (project.PartnerInChargeId is int partnerId &&
            partnerId > 0 &&
            partnerId != project.ManagerId)
            yield return partnerId;
    }

    /// <summary>Usuarios firma (manager/socio) ante solicitud de aprobación antes de enviar al portal cliente.</summary>
    private static List<int> ResolveManagerPartnerRecipientsForPublishApprovalNotice(
        Project project,
        int? publishApprovalRequestedByUserId)
    {
        var recipients = ManagerPartnerRecipientIds(project)
            .Where(u =>
                u > 0 &&
                (!publishApprovalRequestedByUserId.HasValue || u != publishApprovalRequestedByUserId.Value))
            .Distinct()
            .ToList();

        if (recipients.Count == 0 &&
            publishApprovalRequestedByUserId is int rq &&
            rq != project.ManagerId &&
            project.ManagerId > 0)
            recipients.Add(project.ManagerId);

        return recipients.Where(u => u > 0).Distinct().ToList();
    }

    /// <summary>
    /// Un sólo correo para managers/socio tras proceso masivo de «solicitar aprobación publicación cliente» (evita uno por solicitud).
    /// </summary>
    private async Task SendAggregatedManagersPublishApprovalPendingEmailsAsync(
        int firmId,
        List<int> succeededRequestIds,
        CancellationToken cancellationToken)
    {
        var ids = succeededRequestIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _context.Requests.AsNoTracking()
            .Include(r => r.Project)!.ThenInclude(p => p.Customer)
            .Where(r => ids.Contains(r.RequestId))
            .OrderBy(r => r.RequestId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return;

        var recipientUidSet = new HashSet<int>();
        foreach (var entity in rows)
        {
            if (entity.Project == null)
                continue;
            foreach (var u in ResolveManagerPartnerRecipientsForPublishApprovalNotice(
                         entity.Project, entity.PublishApprovalRequestedByUserId))
                recipientUidSet.Add(u);
        }

        if (recipientUidSet.Count == 0)
            return;

        var emailSet = (await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipientUidSet, cancellationToken))
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (emailSet.Count == 0)
            return;

        var tableRows = new List<string[]>(rows.Count);
        foreach (var r in rows)
        {
            var plain = FormatTitlesPlain(r.Title, r.TitleEn);
            var titleEsc = WebUtility.HtmlEncode(plain);
            var href = HandoffLinkForFirm(r.RequestGuid);
            var safeHref = WebUtility.HtmlEncode(href);

            var customerLabel = WebUtility.HtmlEncode((r.Project?.Customer?.Name ?? "—").Trim());
            var p = r.Project;
            string projectLabel = "—";
            if (p != null)
            {
                var name = (p.Name ?? "").Trim();
                var code = (p.ProjectCode ?? "").Trim();
                projectLabel =
                    $"{(string.IsNullOrEmpty(code) ? "" : $"{code} · ")}{name}".Trim().TrimEnd('·', ' ');
                if (string.IsNullOrWhiteSpace(projectLabel))
                    projectLabel = "—";
            }

            var projectEsc = WebUtility.HtmlEncode(projectLabel.Trim());
            var idCell =
                $$"""<span style="font-weight:700;color:{{AmperaEmailPalette.TealOscuro}};">{{WebUtility.HtmlEncode(r.RequestNumber.ToString())}}</span>""";
            var titleCell =
                $"<a href=\"{safeHref}\" style=\"color:{AmperaEmailPalette.TealOscuro};font-weight:600;text-decoration:none;border-bottom:1px solid {AmperaEmailPalette.TealClaro};\">{titleEsc}</a>";

            tableRows.Add([idCell, customerLabel, projectEsc, titleCell]);
        }

        var tableHtml = AmperaBrandedEmail.BuildBulkRequestDataTableHtml(
            ["#", "Cliente auditado", "Proyecto / encargo", "Solicitud"],
            tableRows);

        var queueLink = HandoffLinkForFirmClientPublishQueue();
        var intro = rows.Count == 1
            ? AmperaBrandedEmail.BodyParagraph(
                "Un miembro del equipo solicitó publicar esta solicitud en el portal del cliente. Puede usar la vista «Publicación / aprobaciones» o enlazar desde el listado.")
            : AmperaBrandedEmail.BodyParagraph(
                $"Hay <strong>{rows.Count}</strong> solicitudes con envío pendiente de su aprobación antes de que aparezcan en el portal del cliente. Revise desde la vista «Publicación / aprobaciones» o usando el siguiente detalle:");

        var bodyInnerHtml = $"{intro}\n{tableHtml}";
        var cardHeadline = rows.Count == 1
            ? "Solicitud lista para revisión antes de enviar al cliente"
            : $"{rows.Count} solicitudes pendientes de su aprobación (envío masivo)";
        var subject = rows.Count == 1
            ? $"Aprobar envío al cliente: {FormatTitlesPlain(rows[0].Title, rows[0].TitleEn)}"
            : $"Aprobar envío al cliente: {rows.Count} solicitudes (procesamiento masivo)";

        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Aprobaciones requeridas — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: cardHeadline,
            bodyInnerHtml: bodyInnerHtml,
            primaryButtonHref: queueLink,
            primaryButtonText: "Abrir cola Publicación / aprobaciones",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(queueLink));

        try
        {
            await _emailService.SendHtmlAsync(emailSet, subject, html, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "No se pudo enviar el correo agregado (pendientes de aprobación publish) tras lote masivo");
        }
    }

    /// <summary>
    /// Un sólo correo por dirección destino en portal cliente cuando se publican varias solicitudes en masa
    /// (solo incluye cada solicitud en el cuerpo del correo si ese destinatario la recibiría en el flujo individual).
    /// </summary>
    private async Task SendAggregatedClientPortalPublishedEmailsAsync(
        int firmId,
        List<int> succeededRequestIds,
        CancellationToken cancellationToken)
    {
        var ids = succeededRequestIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var rows = await _context.Requests.AsNoTracking()
            .Include(r => r.Project)
            .ThenInclude(p => p!.Customer)
            .Where(r => ids.Contains(r.RequestId))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return;

        var buckets =
            new Dictionary<string, Dictionary<int, Request>>(StringComparer.OrdinalIgnoreCase);

        void AddToRecipientBucket(string normalizedEmail, Request r)
        {
            var mail = normalizedEmail.Trim();
            if (string.IsNullOrEmpty(mail))
                return;
            if (!buckets.TryGetValue(mail, out var byReqId))
            {
                byReqId = [];
                buckets[mail] = byReqId;
            }

            byReqId[r.RequestId] = r;
        }

        foreach (var entity in rows)
        {
            if (entity.Project?.CustomerId is not int cid)
                continue;

            var userIds =
                await RequestNotificationRecipients.GetClientPortalUserIdsAsync(_context, firmId, cid, entity.RequestId, cancellationToken);
            if (userIds.Count == 0)
                continue;

            var mailPairs = await _context.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.UserId) && u.Email != null && u.Email.Trim() != "")
                .Select(u => u.Email.Trim())
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var em in mailPairs)
                AddToRecipientBucket(em, entity);
        }

        foreach (var kv in buckets)
        {
            var reqsOrdered = kv.Value.Values.OrderBy(x => x.RequestId).ToList();
            if (reqsOrdered.Count == 0)
                continue;

            var tableRows = new List<string[]>(reqsOrdered.Count);
            foreach (var r in reqsOrdered)
            {
                var plain = FormatTitlesPlain(r.Title, r.TitleEn);
                var titleEsc = WebUtility.HtmlEncode(plain);
                var href = HandoffLinkForClient(r.RequestGuid);
                var safeHref = WebUtility.HtmlEncode(href);
                var customerLabel =
                    WebUtility.HtmlEncode((r.Project?.Customer?.Name ?? "—").Trim());

                var idCell =
                    $$"""<span style="font-weight:700;color:{{AmperaEmailPalette.TealOscuro}};">{{WebUtility.HtmlEncode(r.RequestNumber.ToString())}}</span>""";
                var titleCell =
                    $"<a href=\"{safeHref}\" style=\"color:{AmperaEmailPalette.TealOscuro};font-weight:600;text-decoration:none;border-bottom:1px solid {AmperaEmailPalette.TealClaro};\">{titleEsc}</a>";

                tableRows.Add([idCell, customerLabel, titleCell]);
            }

            var tableHtml = AmperaBrandedEmail.BuildBulkRequestDataTableHtml(
                ["#", "Empresa", "Solicitud"],
                tableRows);

            var intro = reqsOrdered.Count == 1
                ? AmperaBrandedEmail.BodyParagraph(
                    "La firma ha publicado una nueva solicitud de documentación en su portal:")
                : AmperaBrandedEmail.BodyParagraph(
                    $"La firma ha publicado <strong>{reqsOrdered.Count}</strong> solicitudes de documentación en su portal:");
            var bodyInnerHtml = $"{intro}\n{tableHtml}";
            var cardHeadline = reqsOrdered.Count == 1
                ? "Nueva solicitud de documentación"
                : $"{reqsOrdered.Count} nuevas solicitudes de documentación";
            var subject = reqsOrdered.Count == 1
                ? $"Nueva solicitud: {FormatTitlesPlain(reqsOrdered[0].Title, reqsOrdered[0].TitleEn)}"
                : $"{reqsOrdered.Count} nuevas solicitudes publicadas por la firma";
            var firstLink = HandoffLinkForClient(reqsOrdered[0].RequestGuid);

            var html = AmperaBrandedEmail.BuildNotificationDocument(
                htmlPageTitle: "Solicitudes en el portal — AMPERA",
                headerSubtitle: "Portal del cliente",
                cardHeadline: cardHeadline,
                bodyInnerHtml: bodyInnerHtml,
                primaryButtonHref: firstLink,
                primaryButtonText: reqsOrdered.Count == 1 ? "Abrir en el portal cliente" : "Abrir la primera solicitud",
                fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(firstLink));

            try
            {
                await _emailService.SendHtmlAsync([kv.Key.Trim()], subject, html, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo SMTP agregado (publish-to-client cliente) tras lote masivo");
            }
        }
    }

    private async Task NotifyManagersPublishApprovalPendingAsync(
        Request entity,
        int firmId,
        CancellationToken cancellationToken,
        bool sendSmtp = true)
    {
        if (entity.Project == null)
            return;

        var recipients = ResolveManagerPartnerRecipientsForPublishApprovalNotice(entity.Project, entity.PublishApprovalRequestedByUserId);
        if (recipients.Count == 0)
            return;

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId,
            recipients,
            "Aprobar envío de solicitud al cliente",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForFirmRequest(entity.RequestGuid),
            entity.RequestId,
            NotificationKind.PublishToClientApprovalPending,
            cancellationToken);

        if (!sendSmtp)
            return;

        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipients, cancellationToken);
        if (emails.Count == 0)
            return;

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var linkFirm = HandoffLinkForFirm(entity.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Aprobación requerida — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: "Solicitud lista para revisión antes de enviar al cliente",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"Un miembro del equipo solicitó publicar «<strong>{titleEsc}</strong>» en el portal del cliente. Abra la solicitud y use «Enviar al cliente» como manager o socio para aprobar y publicar."),
            primaryButtonHref: linkFirm,
            primaryButtonText: "Abrir solicitud en el portal de la firma",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkFirm));

        try
        {
            await _emailService.SendHtmlAsync(
                emails.ToHashSet(StringComparer.OrdinalIgnoreCase),
                $"Aprobar envío al cliente: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo (pendiente de aprobación publish) solicitud {RequestId}", entity.RequestId);
        }
    }

    private async Task NotifyPublishApprovalRejectedToRequestorAsync(
        Request entity,
        int firmId,
        int requestorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var trimmedReason = reason.Trim();
        var reasonEsc = WebUtility.HtmlEncode(trimmedReason);
        var recipients = new[] { requestorUserId };
        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId,
            recipients,
            "Envío al cliente rechazado",
            $"Motivo: {trimmedReason}. {NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn))}",
            AppLinkPathForFirmRequest(entity.RequestGuid),
            entity.RequestId,
            NotificationKind.PublishToClientApprovalPending,
            cancellationToken);

        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipients, cancellationToken);
        if (emails.Count == 0)
            return;

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var linkFirm = HandoffLinkForFirm(entity.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Actualización de solicitud — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: "El envío al cliente no fue aprobado",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"El manager o socio no aprobó (por ahora) publicar «<strong>{titleEsc}</strong>» en el portal del cliente. La solicitud sigue en borrador.<br /><br /><strong>Motivo:</strong> {reasonEsc}"),
            primaryButtonHref: linkFirm,
            primaryButtonText: "Abrir solicitud",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkFirm));

        try
        {
            await _emailService.SendHtmlAsync(
                emails.ToHashSet(StringComparer.OrdinalIgnoreCase),
                $"Envío rechazado: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar rechazo publish-approval a usuario {Uid}", requestorUserId);
        }
    }

    private async Task SendPublishedToClientNotificationsAsync(
        Request entity,
        int firmId,
        CancellationToken cancellationToken,
        bool sendSmtp = true)
    {
        var customerId = entity.Project?.CustomerId;
        if (customerId is not int cid)
            return;

        var recipients = await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
            _context, firmId, cid, entity.RequestId, cancellationToken);

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId,
            recipients,
            "Nueva solicitud de documentación",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForClientRequest(entity.RequestGuid),
            entity.RequestId,
            NotificationKind.PublishedToClient,
            cancellationToken);

        if (!sendSmtp)
            return;

        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, recipients, cancellationToken);
        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var linkClient = HandoffLinkForClient(entity.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: $"Nueva solicitud — AMPERA",
            headerSubtitle: "Portal del cliente",
            cardHeadline: "Nueva solicitud de documentación",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"La firma ha publicado una nueva solicitud de documentación: <strong>{titleEsc}</strong>."),
            primaryButtonHref: linkClient,
            primaryButtonText: "Abrir en el portal cliente",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkClient));

        try
        {
            await _emailService.SendHtmlAsync(
                emails,
                $"Nueva solicitud: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo (publish-to-client) para la solicitud {RequestId}", entity.RequestId);
        }
    }

    /// <summary>El cliente envía la documentación a la firma (notificación por correo a contacto y PM).</summary>
    [HttpPost("{id:int}/send-to-firm")]
    public async Task<IActionResult> SendToFirm(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, id, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.FirmContact)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Manager)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity == null) return NotFound();

        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        if (entity.Status == 0)
            return BadRequest("La solicitud aún está en borrador. Espere a que la firma la publique en el portal.");

        var h = entity.PortalHandoffStatus;
        if (h != (int)RequestPortalHandoff.WithClient && h != (int)RequestPortalHandoff.ReturnedToClient)
            return BadRequest("La solicitud ya está en revisión por la firma.");

        entity.PortalHandoffStatus = (int)RequestPortalHandoff.WithFirm;
        entity.Status = 5;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Flow, "El cliente envió la documentación a revisión de la firma.");
        await _context.SaveChangesAsync(cancellationToken);

        var firmRecipients = await RequestNotificationRecipients.GetFirmStaffUserIdsAsync(
            _context,
            firmId.Value,
            id,
            entity.ProjectId,
            entity.FirmContactId,
            cancellationToken);

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId.Value,
            firmRecipients,
            "El cliente envió una solicitud a revisión",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForFirmRequest(entity.RequestGuid),
            id,
            NotificationKind.ClientSentToFirm,
            cancellationToken);

        var emailList = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, firmRecipients, cancellationToken);
        var to = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var em in emailList)
        {
            if (!string.IsNullOrWhiteSpace(em))
                to.Add(em.Trim());
        }

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var customerPlain = (entity.Project?.Customer?.Name ?? "").Trim();
        var customerEsc = WebUtility.HtmlEncode(customerPlain);
        var linkFirm = HandoffLinkForFirm(entity.RequestGuid);
        var inner = string.IsNullOrEmpty(customerEsc)
            ? AmperaBrandedEmail.BodyParagraph(
                $"El cliente ha enviado la solicitud <strong>{titleEsc}</strong> para su revisión en el portal.")
            : AmperaBrandedEmail.BodyParagraph(
                $"El cliente <strong>{customerEsc}</strong> ha enviado la solicitud <strong>{titleEsc}</strong> para su revisión en el portal.");
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: $"Solicitud en revisión — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: "Documentación enviada a revisión",
            bodyInnerHtml: inner,
            primaryButtonHref: linkFirm,
            primaryButtonText: "Abrir solicitud en el portal de la firma",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkFirm));

        try
        {
            await _emailService.SendHtmlAsync(
                to,
                $"Solicitud enviada a la firma: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // El flujo ya quedó en “con la firma”; el correo es complementario.
            logger.LogWarning(ex, "No se pudo enviar el correo (send-to-firm) para la solicitud {RequestId}", id);
        }

        return NoContent();
    }

    /// <summary>El cliente retira el envío a la firma antes de que se revise documentación (vuelve a poder editar adjuntos).</summary>
    [HttpPost("{id:int}/cancel-send-to-firm")]
    public async Task<IActionResult> CancelSendToFirm(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (!await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestAccessibleToUserAsync(_context, id, userId.Value, firmId.Value, User.GetCustomerId()))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.FirmContact)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Manager)
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity == null) return NotFound();

        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        if (entity.PortalHandoffStatus != (int)RequestPortalHandoff.WithFirm)
            return BadRequest("Solo puede cancelar el envío mientras la solicitud está en revisión por la firma.");

        var reviewedByFirm = await _context.RequestAttachments.AnyAsync(
            a => a.RequestId == id && !a.IsDeleted &&
                 (a.FileStatus == AttachmentStatus.Accepted || a.FileStatus == AttachmentStatus.Rejected),
            cancellationToken);
        if (reviewedByFirm)
            return BadRequest("No puede cancelar el envío porque la firma ya revisó uno o más adjuntos.");

        entity.PortalHandoffStatus = (int)RequestPortalHandoff.WithClient;
        entity.Status = 2;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Flow,
            "El cliente canceló el envío a la firma para corregir la documentación.");
        await _context.SaveChangesAsync(cancellationToken);

        var firmRecipients = await RequestNotificationRecipients.GetFirmStaffUserIdsAsync(
            _context,
            firmId.Value,
            id,
            entity.ProjectId,
            entity.FirmContactId,
            cancellationToken);

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId.Value,
            firmRecipients,
            "El cliente canceló el envío a revisión",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForFirmRequest(entity.RequestGuid),
            id,
            NotificationKind.ClientCancelledSendToFirm,
            cancellationToken);

        var emailList = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, firmRecipients, cancellationToken);
        var to = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var em in emailList)
        {
            if (!string.IsNullOrWhiteSpace(em))
                to.Add(em.Trim());
        }

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var customerPlain = (entity.Project?.Customer?.Name ?? "").Trim();
        var customerEsc = WebUtility.HtmlEncode(customerPlain);
        var linkFirm = HandoffLinkForFirm(entity.RequestGuid);
        var inner = string.IsNullOrEmpty(customerEsc)
            ? AmperaBrandedEmail.BodyParagraph(
                $"El cliente ha <strong>cancelado el envío</strong> de la solicitud <strong>{titleEsc}</strong> que estaba en revisión. Puede volver a enviarla cuando corrija la documentación.")
            : AmperaBrandedEmail.BodyParagraph(
                $"El cliente <strong>{customerEsc}</strong> ha <strong>cancelado el envío</strong> de la solicitud <strong>{titleEsc}</strong> que estaba en revisión. Puede volver a enviarla cuando corrija la documentación.");
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: $"Envío cancelado — AMPERA",
            headerSubtitle: "Portal de la firma",
            cardHeadline: "El cliente retiró el envío a revisión",
            bodyInnerHtml: inner,
            primaryButtonHref: linkFirm,
            primaryButtonText: "Abrir solicitud en el portal de la firma",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkFirm));

        try
        {
            await _emailService.SendHtmlAsync(
                to,
                $"Envío cancelado por el cliente: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo (cancel-send-to-firm) para la solicitud {RequestId}", id);
        }

        return NoContent();
    }

    /// <summary>La firma devuelve la solicitud al cliente (notificación por correo a usuarios portal del cliente).</summary>
    [HttpPost("{id:int}/send-to-client")]
    public async Task<IActionResult> SendToClient(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is IActionResult forbidStc)
            return forbidStc;
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity == null) return NotFound();

        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        if (entity.PortalHandoffStatus != (int)RequestPortalHandoff.WithFirm)
            return BadRequest("Solo puede devolver al cliente una solicitud que esté en revisión por la firma.");

        entity.PortalHandoffStatus = (int)RequestPortalHandoff.ReturnedToClient;
        entity.Status = 2;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Flow, "La firma devolvió la solicitud al cliente para revisión o corrección.");
        await _context.SaveChangesAsync(cancellationToken);

        var customerId = entity.Project?.CustomerId;
        if (customerId is not int cid)
            return NoContent();

        var clientRecipients = await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
            _context, firmId.Value, cid, id, cancellationToken);

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId.Value,
            clientRecipients,
            "La firma devolvió una solicitud",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForClientRequest(entity.RequestGuid),
            id,
            NotificationKind.FirmReturnedToClient,
            cancellationToken);

        var clientEmails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, clientRecipients, cancellationToken);

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var linkClient = HandoffLinkForClient(entity.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: $"Actualización de solicitud — AMPERA",
            headerSubtitle: "Portal del cliente",
            cardHeadline: "La firma devolvió su solicitud",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"La firma ha devuelto la solicitud <strong>{titleEsc}</strong>. Revise el estado de los adjuntos en el portal de cliente."),
            primaryButtonHref: linkClient,
            primaryButtonText: "Abrir solicitud en el portal cliente",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkClient));

        try
        {
            await _emailService.SendHtmlAsync(
                clientEmails,
                $"Actualización de su solicitud: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo (send-to-client) para la solicitud {RequestId}", id);
        }

        return NoContent();
    }

    /// <summary>
    /// La firma marca la solicitud como rechazada o cancelada mientras está en revisión (el cliente sera notificado; puede volver a enviar cuando corresponda).
    /// </summary>
    [HttpPost("{id:int}/decline-request")]
    public async Task<IActionResult> DeclineRequestByFirm(int id, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is IActionResult forbidDec)
            return forbidDec;
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests
            .Include(r => r.Project)!
                .ThenInclude(p => p.Customer)
            .FirstOrDefaultAsync(r => r.RequestId == id, cancellationToken);
        if (entity == null) return NotFound();

        if (entity.ClosedAt != null)
            return BadRequest("La solicitud está cerrada.");

        if (entity.PortalHandoffStatus != (int)RequestPortalHandoff.WithFirm)
            return BadRequest("Solo puede rechazar o cancelar una solicitud que esté en revisión por la firma.");

        entity.PortalHandoffStatus = (int)RequestPortalHandoff.ReturnedToClient;
        entity.Status = 3;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Flow,
            "La firma marcó la solicitud como rechazada / cancelada (desde revisión en curso).");
        await _context.SaveChangesAsync(cancellationToken);

        var customerId = entity.Project?.CustomerId;
        if (customerId is not int cid)
            return NoContent();

        var clientRecipients = await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
            _context, firmId.Value, cid, id, cancellationToken);

        await UserNotificationCommands.CreateManyAsync(
            _context,
            firmId.Value,
            clientRecipients,
            "La firma actualizó el estado de la solicitud",
            NotificationBodyWithCustomerName(entity.Project?.Customer?.Name, FormatTitlesPlain(entity.Title, entity.TitleEn)),
            AppLinkPathForClientRequest(entity.RequestGuid),
            id,
            NotificationKind.FirmDeclinedRequest,
            cancellationToken);

        var clientEmails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, clientRecipients, cancellationToken);

        var titleEsc = WebUtility.HtmlEncode(FormatTitlesPlain(entity.Title, entity.TitleEn));
        var linkClient = HandoffLinkForClient(entity.RequestGuid);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: $"Estado de solicitud — AMPERA",
            headerSubtitle: "Portal del cliente",
            cardHeadline: "Actualización del estado",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"La firma ha marcado como <strong>rechazada o cancelada</strong> la solicitud <strong>{titleEsc}</strong> durante la revisión. Revise el detalle en el portal de cliente si corresponde un nuevo envío."),
            primaryButtonHref: linkClient,
            primaryButtonText: "Abrir solicitud en el portal cliente",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(linkClient));

        try
        {
            await _emailService.SendHtmlAsync(
                clientEmails,
                $"Estado de solicitud: {FormatTitlesPlain(entity.Title, entity.TitleEn)}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo (decline-request) para la solicitud {RequestId}", id);
        }

        return NoContent();
    }

    /// <summary>
    /// La firma da por recibida y cerrada la solicitud PBC, con o sin adjuntos (p. ej. cuando el cliente
    /// indica que no aplica documentación). No exige que todos los adjuntos estén aprobados:
    /// los rechazados pueden conservarse solo como bitácora y las correcciones subirse en archivos nuevos.
    /// </summary>
    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> CloseRequest(int id, CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is IActionResult forbidCl)
            return forbidCl;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (entity == null) return NotFound();
        if (entity.ClosedAt != null)
            return BadRequest("La solicitud ya está cerrada.");
        if (entity.PortalHandoffStatus != (int)RequestPortalHandoff.WithFirm)
            return BadRequest("Solo puede cerrar cuando la solicitud está en revisión por la firma (tras el envío del cliente).");

        entity.ClosedAt = DateTime.UtcNow;
        entity.ClosedById = userId.Value;
        entity.Status = 4;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Closed, "La firma cerró la solicitud como recibida y completa.");
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Revoca el cierre (solo firma). No modifica adjuntos ni el flujo portal.</summary>
    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> ReopenRequest(int id, CancellationToken cancellationToken)
    {
        if (await ForbidUnlessRequestsMutationAsync(ResourcePermissionAction.Update, cancellationToken) is IActionResult forbidRo)
            return forbidRo;

        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return Forbid();
        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, id, firmId.Value))
            return NotFound();

        var entity = await _context.Requests.FirstOrDefaultAsync(r => r.RequestId == id);
        if (entity == null) return NotFound();
        if (entity.ClosedAt == null)
            return BadRequest("La solicitud no está cerrada.");

        entity.ClosedAt = null;
        entity.ClosedById = null;
        entity.Status = entity.PortalHandoffStatus == (int)RequestPortalHandoff.WithFirm ? 5 : 2;
        RequestHistoryLog.Add(_context, id, userId.Value, RequestHistoryLog.Closed, "La firma reabrió la solicitud.");
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string AppLinkPathForFirmRequest(Guid requestGuid) => $"/requests/{requestGuid:D}";

    /// <summary>Texto para asuntos y notificaciones: ES / EN cuando ambos existen.</summary>
    private static string FormatTitlesPlain(string? title, string? titleEn)
    {
        var es = (title ?? "").Trim();
        var en = (titleEn ?? "").Trim();
        if (string.IsNullOrEmpty(es)) return en;
        if (string.IsNullOrEmpty(en)) return es;
        return $"{es} / {en}";
    }

    /// <summary>Texto secundario en campana: cliente auditado + título de solicitud.</summary>
    private static string NotificationBodyWithCustomerName(string? customerName, string? requestTitle)
    {
        var cn = string.IsNullOrWhiteSpace(customerName) ? "" : customerName.Trim();
        var rt = (requestTitle ?? "").Trim();
        if (string.IsNullOrEmpty(cn))
            return string.IsNullOrEmpty(rt) ? "—" : rt;
        if (string.IsNullOrEmpty(rt))
            return cn;
        var combined = $"{cn} · {rt}";
        return combined.Length <= 1000 ? combined : combined[..1000];
    }

    /** Prioridades alineadas con locales ES del front. */
    private static string RequestExportPriorityEs(int priority) =>
        priority switch
        {
            0 => "Baja",
            1 => "Media",
            2 => "Alta",
            3 => "Crítica",
            _ => priority.ToString()
        };

    /** Estados alineados con locales ES del front. */
    private static string RequestExportStatusEs(int status) =>
        status switch
        {
            0 => "Borrador",
            1 => "Enviada",
            2 => "Enviada cliente",
            3 => "Rechazada",
            4 => "Recibida / cerrada",
            5 => "En revisión por la firma",
            _ => status.ToString()
        };

    private static string SanitizeExcelFileSlug(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "export";
        var sb = new StringBuilder(capacity: Math.Min(raw.Length, 96));
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }

        var s = sb.ToString().Trim('_').Trim('.');
        if (s.Length > 64)
            s = s[..64];
        return string.IsNullOrEmpty(s) ? "export" : s;
    }

    private static string AppLinkPathForClientRequest(Guid requestGuid) => $"/portal/requests/{requestGuid:D}";

    private string HandoffLinkForFirm(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return "#";
        return $"{baseUrl}/requests/{requestGuid}";
    }

    private string HandoffLinkForClient(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return "#";
        return $"{baseUrl}/portal/requests/{requestGuid}";
    }

    /// <summary>Vista firma masiva (#/requests/client-publish-queue en front).</summary>
    private string HandoffLinkForFirmClientPublishQueue()
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return "#";
        return $"{baseUrl}/requests/client-publish-queue";
    }

    private async Task<IActionResult?> ForbidUnlessRequestsMutationAsync(ResourcePermissionAction action,
        CancellationToken cancellationToken)
    {
        if (User.GetFirmId() is null || User.GetUserId() is null)
            return Unauthorized();
        var uid = User.GetUserId()!.Value;
        if (await TenantQueries.UserIsClientPortalAsync(_context, uid))
            return null;
        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Requests, action,
                cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ForbiddenResourceMessage });
        return null;
    }

    /// <summary>Low, Medium, High, Critical</summary>
    private const int PriorityMin = 0;
    private const int PriorityMax = 3;
    /// <summary>Draft, Sent, SentToClient, Rejected, Accepted, InReviewByFirm</summary>
    private const int StatusMin = 0;
    private const int StatusMax = 5;

    private static bool ValidateRequestCore(Request request, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            error = "El título en español es obligatorio.";
            return false;
        }
        if (request.Title.Length > 250)
        {
            error = "El título en español no puede superar 250 caracteres.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.TitleEn))
        {
            error = "El título en inglés es obligatorio.";
            return false;
        }
        if (request.TitleEn.Length > 250)
        {
            error = "El título en inglés no puede superar 250 caracteres.";
            return false;
        }
        if (request.Priority < PriorityMin || request.Priority > PriorityMax)
        {
            error = "Prioridad no válida.";
            return false;
        }
        if (request.Status < StatusMin || request.Status > StatusMax)
        {
            error = "Estado no válido.";
            return false;
        }
        return true;
    }
}
