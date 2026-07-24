using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text;

namespace AuditWorkflow.Controllers.Core;

[Authorize]
[Route("api/request-reminders")]
[ApiController]
public class RequestRemindersOverviewController(AppDbContext context) : ControllerBase
{
    private const string ExcelExportMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly AppDbContext _context = context;

    /// <summary>
    /// Vista consolidada: vencimiento de solicitud (según proyecto) y recordatorios manuales.
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<object>> GetOverview(
        [FromQuery] int? customerId,
        [FromQuery] int? projectId,
        [FromQuery] string? kind,
        [FromQuery] bool includeClosed = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string search = "",
        CancellationToken cancellationToken = default)
    {
        var access = await ValidateOverviewAccessAsync(customerId, projectId, cancellationToken);
        if (access.ErrorResult != null)
            return access.ErrorResult;

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var allItems = await BuildOverviewItemsAsync(
            access.FirmId!.Value,
            customerId,
            projectId,
            kind,
            includeClosed,
            cancellationToken);

        var filtered = FilterOverviewBySearch(allItems, search);
        var totalRecords = filtered.Count;
        var skip = (page - 1) * pageSize;
        var pageItems = filtered.Skip(skip).Take(pageSize).ToList();

        return Ok(new
        {
            TotalRecords = totalRecords,
            Items = pageItems
        });
    }

    /// <summary>Exporta Excel con los mismos filtros que <see cref="GetOverview"/>.</summary>
    [HttpGet("export-excel")]
    public async Task<IActionResult> ExportOverviewExcel(
        [FromQuery] int? customerId,
        [FromQuery] int? projectId,
        [FromQuery] string? kind,
        [FromQuery] bool includeClosed = false,
        [FromQuery] string search = "",
        CancellationToken cancellationToken = default)
    {
        var access = await ValidateOverviewAccessAsync(customerId, projectId, cancellationToken);
        if (access.ErrorResult != null)
            return access.ErrorResult;

        var firmId = access.FirmId!.Value;
        var allItems = await BuildOverviewItemsAsync(
            firmId,
            customerId,
            projectId,
            kind,
            includeClosed,
            cancellationToken);
        var items = FilterOverviewBySearch(allItems, search);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Recordatorios");
        ws.Cell(1, 1).Value = "Tipo";
        ws.Cell(1, 2).Value = "ID solicitud";
        ws.Cell(1, 3).Value = "Título (ES)";
        ws.Cell(1, 4).Value = "Título (EN)";
        ws.Cell(1, 5).Value = "Cliente";
        ws.Cell(1, 6).Value = "Proyecto";
        ws.Cell(1, 7).Value = "Fecha vencimiento";
        ws.Cell(1, 8).Value = "Días antes";
        ws.Cell(1, 9).Value = "Aviso desde";
        ws.Cell(1, 10).Value = "Aviso hasta";
        ws.Cell(1, 11).Value = "Estado";
        ws.Cell(1, 12).Value = "Destinatarios";
        ws.Cell(1, 13).Value = "Nota";
        ws.Cell(1, 14).Value = "Creado por";
        ws.Cell(1, 15).Value = "Creado (UTC)";
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        var rowIx = 2;
        foreach (var x in items)
        {
            ws.Cell(rowIx, 1).Value = KindLabelEs(x.Kind);
            ws.Cell(rowIx, 2).Value = x.RequestNumber;
            ws.Cell(rowIx, 3).Value = x.RequestTitle;
            ws.Cell(rowIx, 4).Value = x.RequestTitleEn;
            ws.Cell(rowIx, 5).Value = x.CustomerName;
            ws.Cell(rowIx, 6).Value = x.ProjectName;
            WriteDateCell(ws, rowIx, 7, x.DueDate);
            ws.Cell(rowIx, 8).Value = x.DaysBeforeDue;
            WriteDateCell(ws, rowIx, 9, x.NotifyFromDate);
            WriteDateCell(ws, rowIx, 10, x.NotifyUntilDate);
            ws.Cell(rowIx, 11).Value = StatusLabelEs(x);
            ws.Cell(rowIx, 12).Value = TargetsLabelEs(x);
            ws.Cell(rowIx, 13).Value = x.Kind == "RequestDue"
                ? "Según fecha de vencimiento de la solicitud y días del encargo."
                : (x.Note ?? "");
            ws.Cell(rowIx, 14).Value = x.CreatedByName ?? "";
            if (x.CreatedAtUtc.HasValue)
            {
                ws.Cell(rowIx, 15).Value = x.CreatedAtUtc.Value;
                ws.Cell(rowIx, 15).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }
            else
            {
                ws.Cell(rowIx, 15).Clear();
            }

            rowIx++;
        }

        ws.Columns().AdjustToContents(1, 200);

        string fileSlug;
        if (projectId is int pjForName)
        {
            var slug = await _context.Projects.AsNoTracking()
                .Where(p => p.ProjectId == pjForName && p.FirmId == firmId)
                .Select(p => p.ProjectCode ?? p.Name ?? "proyecto")
                .FirstOrDefaultAsync(cancellationToken);
            fileSlug = SanitizeExcelFileSlug(slug ?? "proyecto");
        }
        else
        {
            fileSlug = "todas";
        }

        await using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"recordatorios-{fileSlug}-{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(), ExcelExportMime, fileName);
    }

    private async Task<(int? FirmId, ActionResult? ErrorResult)> ValidateOverviewAccessAsync(
        int? customerId,
        int? projectId,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null)
            return (null, Unauthorized());

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))
            return (null, Forbid());

        if (customerId is int cid && !await TenantQueries.CustomerBelongsToFirmAsync(_context, cid, firmId.Value))
            return (null, BadRequest("Cliente inválido."));

        if (projectId is int pid)
        {
            var projectOk = await _context.Projects.AsNoTracking()
                .AnyAsync(p => p.ProjectId == pid && p.FirmId == firmId.Value, cancellationToken);
            if (!projectOk)
                return (null, BadRequest("Proyecto inválido."));
        }

        return (firmId.Value, null);
    }

    private async Task<List<RequestReminderOverviewDto>> BuildOverviewItemsAsync(
        int firmId,
        int? customerId,
        int? projectId,
        string? kind,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var normalizedKind = (kind ?? "").Trim().ToLowerInvariant();
        var includeRequestDue = normalizedKind is "" or "all" or "requestdue";
        var includeManual = normalizedKind is "" or "all" or "manual";

        var items = new List<RequestReminderOverviewDto>();

        if (includeRequestDue)
        {
            var requestDueQuery = _context.Requests
                .AsNoTracking()
                .Where(r => r.Project!.FirmId == firmId)
                .Where(r => r.DueDate != null)
                .Where(r => r.Project!.DefaultReminderDaysBeforeDue != null);

            if (!includeClosed)
                requestDueQuery = requestDueQuery.Where(r => r.ClosedAt == null);
            if (customerId is int c)
                requestDueQuery = requestDueQuery.Where(r => r.Project!.CustomerId == c);
            if (projectId is int p)
                requestDueQuery = requestDueQuery.Where(r => r.ProjectId == p);

            var requestDueRows = await requestDueQuery
                .Select(r => new
                {
                    r.RequestId,
                    r.RequestNumber,
                    r.RequestGuid,
                    r.Title,
                    r.TitleEn,
                    r.DueDate,
                    r.ClosedAt,
                    CustomerName = r.Project!.Customer!.Name ?? "",
                    ProjectId = r.ProjectId,
                    ProjectName = r.Project!.Name ?? "",
                    ProjectCode = r.Project!.ProjectCode ?? "",
                    DaysBeforeDue = r.Project!.DefaultReminderDaysBeforeDue!.Value
                })
                .ToListAsync(cancellationToken);

            foreach (var row in requestDueRows)
            {
                var dueDate = DateOnly.FromDateTime(row.DueDate!.Value.Date);
                items.Add(new RequestReminderOverviewDto
                {
                    Kind = "RequestDue",
                    RequestReminderId = null,
                    RequestId = row.RequestId,
                    RequestNumber = row.RequestNumber,
                    RequestGuid = row.RequestGuid,
                    RequestTitle = row.Title ?? "",
                    RequestTitleEn = row.TitleEn ?? "",
                    CustomerName = row.CustomerName,
                    ProjectId = row.ProjectId,
                    ProjectName = FormatProjectLabel(row.ProjectCode, row.ProjectName),
                    DueDate = dueDate,
                    DaysBeforeDue = row.DaysBeforeDue,
                    NotifyFromDate = dueDate.AddDays(-row.DaysBeforeDue),
                    NotifyUntilDate = dueDate,
                    IsActive = row.ClosedAt == null,
                    NotifyFirmStaff = true,
                    NotifyClientPortal = true,
                    Note = null,
                    CreatedByName = null,
                    CreatedAtUtc = null,
                    RequestIsClosed = row.ClosedAt != null
                });
            }
        }

        if (includeManual)
        {
            var manualQuery = _context.RequestReminders
                .AsNoTracking()
                .Where(r => r.Request!.Project!.FirmId == firmId);

            if (!includeClosed)
                manualQuery = manualQuery.Where(r => r.Request!.ClosedAt == null);
            if (customerId is int c)
                manualQuery = manualQuery.Where(r => r.Request!.Project!.CustomerId == c);
            if (projectId is int p)
                manualQuery = manualQuery.Where(r => r.Request!.ProjectId == p);

            var manualRows = await manualQuery
                .Select(r => new
                {
                    r.RequestReminderId,
                    r.RequestId,
                    RequestNumber = r.Request!.RequestNumber,
                    RequestGuid = r.Request!.RequestGuid,
                    RequestTitle = r.Request!.Title ?? "",
                    RequestTitleEn = r.Request!.TitleEn ?? "",
                    CustomerName = r.Request!.Project!.Customer!.Name ?? "",
                    ProjectId = r.Request!.ProjectId,
                    ProjectName = r.Request!.Project!.Name ?? "",
                    ProjectCode = r.Request!.Project!.ProjectCode ?? "",
                    r.ReminderDueDate,
                    r.DaysBeforeDue,
                    r.IsActive,
                    r.NotifyFirmStaff,
                    r.NotifyClientPortal,
                    r.Note,
                    CreatedByName = r.CreatedBy!.FullName ?? "",
                    r.CreatedAtUtc,
                    RequestIsClosed = r.Request!.ClosedAt != null
                })
                .ToListAsync(cancellationToken);

            foreach (var row in manualRows)
            {
                var dueDate = DateOnly.FromDateTime(row.ReminderDueDate.Date);
                items.Add(new RequestReminderOverviewDto
                {
                    Kind = "Manual",
                    RequestReminderId = row.RequestReminderId,
                    RequestId = row.RequestId,
                    RequestNumber = row.RequestNumber,
                    RequestGuid = row.RequestGuid,
                    RequestTitle = row.RequestTitle,
                    RequestTitleEn = row.RequestTitleEn,
                    CustomerName = row.CustomerName,
                    ProjectId = row.ProjectId,
                    ProjectName = FormatProjectLabel(row.ProjectCode, row.ProjectName),
                    DueDate = dueDate,
                    DaysBeforeDue = row.DaysBeforeDue,
                    NotifyFromDate = dueDate.AddDays(-row.DaysBeforeDue),
                    NotifyUntilDate = dueDate,
                    IsActive = row.IsActive && !row.RequestIsClosed,
                    NotifyFirmStaff = row.NotifyFirmStaff,
                    NotifyClientPortal = row.NotifyClientPortal,
                    Note = row.Note,
                    CreatedByName = string.IsNullOrWhiteSpace(row.CreatedByName) ? null : row.CreatedByName.Trim(),
                    CreatedAtUtc = row.CreatedAtUtc,
                    RequestIsClosed = row.RequestIsClosed
                });
            }
        }

        return items
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.Kind)
            .ThenByDescending(i => i.RequestNumber)
            .ThenByDescending(i => i.RequestId)
            .ToList();
    }

    private static List<RequestReminderOverviewDto> FilterOverviewBySearch(
        IReadOnlyList<RequestReminderOverviewDto> items,
        string? search)
    {
        var term = (search ?? "").Trim();
        if (term.Length == 0)
            return items.ToList();

        var lower = term.ToLowerInvariant();
        return items.Where(i =>
                i.RequestNumber.ToString().Contains(lower)
                || i.RequestTitle.ToLowerInvariant().Contains(lower)
                || i.RequestTitleEn.ToLowerInvariant().Contains(lower)
                || i.CustomerName.ToLowerInvariant().Contains(lower)
                || i.ProjectName.ToLowerInvariant().Contains(lower)
                || KindLabelEs(i.Kind).ToLowerInvariant().Contains(lower)
                || (i.Note ?? "").ToLowerInvariant().Contains(lower)
                || (i.CreatedByName ?? "").ToLowerInvariant().Contains(lower))
            .ToList();
    }

    private static void WriteDateCell(IXLWorksheet ws, int rowIx, int colIx, DateOnly date)
    {
        ws.Cell(rowIx, colIx).Value = new DateTime(date.Year, date.Month, date.Day);
        ws.Cell(rowIx, colIx).Style.DateFormat.Format = "yyyy-mm-dd";
    }

    private static string KindLabelEs(string kind) =>
        kind switch
        {
            "RequestDue" => "Vencimiento solicitud",
            "Manual" => "Manual",
            _ => kind
        };

    private static string StatusLabelEs(RequestReminderOverviewDto x)
    {
        if (x.RequestIsClosed)
            return "Solicitud cerrada";
        return x.IsActive ? "Activo" : "Inactivo";
    }

    private static string TargetsLabelEs(RequestReminderOverviewDto x)
    {
        var parts = new List<string>();
        if (x.NotifyFirmStaff) parts.Add("Equipo firma");
        if (x.NotifyClientPortal) parts.Add("Portal cliente");
        return parts.Count == 0 ? "—" : string.Join(" / ", parts);
    }

    private static string FormatProjectLabel(string? code, string? name)
    {
        var c = (code ?? "").Trim();
        var n = (name ?? "").Trim();
        if (c.Length > 0 && n.Length > 0) return $"{c} — {n}";
        return c.Length > 0 ? c : n;
    }

    private static string SanitizeExcelFileSlug(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "export";
        var sb = new StringBuilder(capacity: Math.Min(raw.Length, 96));
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch))
                sb.Append('-');
        }

        var s = sb.ToString().Trim('-', '.');
        return s.Length == 0 ? "export" : (s.Length > 48 ? s[..48] : s);
    }

    public sealed class RequestReminderOverviewDto
    {
        /// <summary>RequestDue = vencimiento de la solicitud; Manual = recordatorio agregado.</summary>
        public string Kind { get; set; } = "";

        public int? RequestReminderId { get; set; }

        public int RequestId { get; set; }

        public int RequestNumber { get; set; }

        public Guid RequestGuid { get; set; }

        public string RequestTitle { get; set; } = "";

        public string RequestTitleEn { get; set; } = "";

        public string CustomerName { get; set; } = "";

        public int ProjectId { get; set; }

        public string ProjectName { get; set; } = "";

        public DateOnly DueDate { get; set; }

        public int DaysBeforeDue { get; set; }

        public DateOnly NotifyFromDate { get; set; }

        public DateOnly NotifyUntilDate { get; set; }

        public bool IsActive { get; set; }

        public bool NotifyFirmStaff { get; set; }

        public bool NotifyClientPortal { get; set; }

        public string? Note { get; set; }

        public string? CreatedByName { get; set; }

        public DateTime? CreatedAtUtc { get; set; }

        public bool RequestIsClosed { get; set; }
    }
}
