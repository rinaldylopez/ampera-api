using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AuditWorkflow.Controllers.Core;

/// <summary>Plantilla Excel e importación masiva de solicitudes (PBC) para un proyecto concreto.</summary>
[Authorize]
[Route("api/projects/{projectId:int}/requests/excel")]
[ApiController]
public class ProjectRequestExcelImportController(
    AppDbContext context,
    IProfileResourcePermissionService resourcePermissions,
    ILogger<ProjectRequestExcelImportController> logger) : ControllerBase
{
    /// <remarks>Encabezados contiguos: la plantilla debe conservar orden y texto base.</remarks>
    private static readonly string[] ExpectedHeaders =
    [
        "Titulo_es",
        "Titulo_en",
        "Email_contacto_firma",
        "Descripcion_opcional",
        "Fecha_limite_yyyy-mm-dd_opcional",
        "Prioridad_0_o_texto"
    ];

    /** Plantillas antiguas con columna Estado; se aceptan pero el valor se ignora (siempre borrador). */
    private static readonly string[] LegacyExpectedHeaders =
    [
        "Titulo_es",
        "Titulo_en",
        "Email_contacto_firma",
        "Descripcion_opcional",
        "Fecha_limite_yyyy-mm-dd_opcional",
        "Prioridad_0_o_texto",
        "Estado_0_o_texto"
    ];

    private const int ImportStatusDraft = 0;

    private const string MimeXlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /** Correo solo ilustrativo en la plantilla; no debe importarse ni sustituir a un usuario real. */
    private const string TemplateFirmContactEmailExample = "ejemplo@ampera.com";

    /// <remarks>Variante en inglés por si llega desde plantillas viejas copiadas.</remarks>
    private static readonly string[] ForbiddenPlaceholderEmails =
    [
        "ejemplo@ampera.com",
        "example@ampera.com"
    ];

    private readonly AppDbContext _context = context;
    private readonly IProfileResourcePermissionService _resourcePermissions = resourcePermissions;
    private readonly ILogger<ProjectRequestExcelImportController> _logger = logger;

    [HttpGet("prerequisites")]
    public async Task<ActionResult<object>> GetPrerequisites(int projectId)
    {
        var firmId = User.GetFirmId();
        var uid = User.GetUserId();
        if (firmId is null || uid is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, uid.Value))
            return Forbid();

        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        return Ok(await BuildPrerequisiteSnapshotAsync(firmId.Value));
    }

    [HttpGet("template")]
    public async Task<ActionResult> DownloadTemplate(int projectId)
    {
        var firmId = User.GetFirmId();
        var uid = User.GetUserId();
        if (firmId == null || uid == null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, uid.Value))
            return Forbid();

        var project = await _context.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.FirmId == firmId.Value);
        if (project == null) return NotFound();

        await using var stream = GenerateTemplateExcel(project.ProjectCode ?? "PROY", project.Name ?? "",
            project.ProjectId, ExpectedHeaders);

        var name = $"{SanitizeFile(project.ProjectCode ?? "proyecto")}-plantilla-solicitudes.xlsx";
        return File(stream.ToArray(), MimeXlsx, name);
    }

    [HttpPost("preview")]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_165_824)]
    [RequestSizeLimit(25_165_824)]
    public Task<ActionResult<object>> PreviewSpreadsheet(int projectId, IFormFile file, CancellationToken cancellationToken) =>
        ProcessSpreadsheetAsync(projectId, file, persist: false, cancellationToken);

    [HttpPost("import")]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_165_824)]
    [RequestSizeLimit(25_165_824)]
    public Task<ActionResult<object>> ImportSpreadsheet(int projectId, IFormFile file, CancellationToken cancellationToken) =>
        ProcessSpreadsheetAsync(projectId, file, persist: true, cancellationToken);

    private async Task<ActionResult<object>> ProcessSpreadsheetAsync(int projectId, IFormFile file, bool persist,
        CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var actorId = User.GetUserId();
        if (firmId == null || actorId == null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, actorId.Value))
            return Forbid();

        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Requests,
                ResourcePermissionAction.Create,
                cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "No tiene permiso para esta operación." });

        if (!await TenantQueries.ProjectBelongsToFirmAsync(_context, projectId, firmId.Value))
            return NotFound();

        if (file == null || file.Length == 0)
            return BadRequest("Adjunte un archivo Excel (.xlsx).");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo se admiten archivos .xlsx.");

        var blockers = await GetBlockersAsync(firmId.Value, cancellationToken);
        if (blockers.Count > 0)
            return BadRequest(new { message = "Hay requisitos pendientes antes de poder importar.", blockerCodes = blockers });

        XLWorkbook wb;
        await using (var copied = new MemoryStream())
        {
            await file.CopyToAsync(copied, cancellationToken);
            copied.Position = 0;
            try { wb = new XLWorkbook(copied); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archivo excel inválido, proyecto {Id}", projectId);
                return BadRequest("El archivo Excel no se pudo leer.");
            }
        }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(w =>
                         string.Equals(w.Name, "Solicitudes", StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheet(1);

            IReadOnlyList<string> matchedHeaders = ExpectedHeaders;
            if (!TryLocateContiguousHeaders(ws, ExpectedHeaders, out var hdrRow, out var c0, out _))
            {
                if (!TryLocateContiguousHeaders(ws, LegacyExpectedHeaders, out hdrRow, out c0, out var errHdr))
                    return BadRequest(errHdr ?? "Encabezados no reconocidos. Descargue de nuevo la plantilla.");
                matchedHeaders = LegacyExpectedHeaders;
            }

            var lastColUsed = ws.LastColumnUsed()?.ColumnNumber() ?? c0;

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? hdrRow;

            List<object> rowErr = [];

            /** Filas vistas en la UI de previsualización (una por fila de datos analizada que no está vacía). */
            List<object> previewRows = [];

            /** Solicitudes válidas listas para insertar (solo en modo persist, tras cerrar la fila Excel). */
            List<(int ExcelRow, string TitEs, string TitEn, string Html, DateTime? DueUtc, int Prio,
                int FirmContactId)> pendingAdds = [];

            var used = 0;
            var inserted = 0;
            var insertedRequestIds = new List<int>();
            var validRows = 0;

            static string GV(IXLWorksheet ww, int row, int cc) =>
                NormalizeCellText(ww.Cell(row, cc));

            for (var r = hdrRow + 1; r <= lastRow; r++)
            {
                var cellRangeEnd = Math.Max(c0 + matchedHeaders.Count - 1, lastColUsed);
                if (IsRowEmpty(ws, r, cellRangeEnd))
                    continue;

                used++;

                var titEs = GV(ws, r, c0 + 0);
                var titEn = GV(ws, r, c0 + 1);
                var mail = GV(ws, r, c0 + 2).Trim().ToLower();
                var descr = GV(ws, r, c0 + 3);
                var dueTxt = GV(ws, r, c0 + 4);
                var priTxt = GV(ws, r, c0 + 5);

                if (string.IsNullOrWhiteSpace(titEs) && string.IsNullOrWhiteSpace(titEn) && string.IsNullOrWhiteSpace(mail))
                {
                    used--;
                    continue;
                }

                string? rowFail = null;
                int prio = default;
                DateTime? dueUtc = null;
                User? matchedUser = null;

                void AddFail(string msg)
                {
                    rowFail ??= msg;
                }

                if (!TitlesOk(titEs, titEn, out var vt))
                    AddFail(vt);
                else if (mail.Length == 0 || mail.Split('@').Length != 2)
                    AddFail("Email_contacto_firma inválido.");
                else if (ForbiddenPlaceholderEmails.Contains(mail, StringComparer.OrdinalIgnoreCase))
                    AddFail(
                        $"«{TemplateFirmContactEmailExample}» (o similar de la plantilla) es solo ejemplo: sustitúyalo por el correo de un usuario FIRMA ACTIVO de su empresa registrado en AMPERA.");
                else if (!TryParsePri(priTxt, out prio, out var pe))
                    AddFail(pe ?? "Prioridad inválida.");
                else if (!string.IsNullOrWhiteSpace(dueTxt))
                {
                    var cellDue = ws.Cell(r, c0 + 4);
                    if (!TryParseDueDate(cellDue, dueTxt, out var de, out var du))
                        AddFail(de ?? "Fecha inválida.");
                    else dueUtc = du;
                }

                if (rowFail == null)
                {
                    matchedUser = await TenantQueries.FindActiveFirmStaffByEmailInFirmAsync(
                        _context, firmId.Value, mail, cancellationToken);
                    if (matchedUser == null)
                        rowFail =
                            $"No existe usuario firma activo asociado a ESTA firma con el correo «{mail}». Use el correo exacto de un usuario interno dado de alta en la firma con la que está trabajando (no portal cliente).";
                }

                if (rowFail != null)
                    rowErr.Add(new { row = r, message = rowFail });
                else
                    validRows++;

                previewRows.Add(new
                {
                    excelRow = r,
                    titleEs = TruncateUi(titEs, 220),
                    titleEn = TruncateUi(titEn, 220),
                    emailFirmContact = mail,
                    firmContactName = matchedUser == null ? (string?)null : (matchedUser.FullName ?? ""),
                    priority = rowFail != null ? (int?)null : prio,
                    dueUtc = rowFail != null ? (DateTime?)null : dueUtc,
                    descriptionPreview = TruncateUi(descr, 160),
                    isValid = rowFail == null,
                    validationMessage = rowFail
                });

                if (persist && rowFail == null && matchedUser is not null)
                    pendingAdds.Add((r, titEs, titEn, PlainParagraphsHtml(descr), dueUtc, prio, matchedUser.UserId));
            }

            if (!persist)
            {
                return Ok(new
                {
                    scannedDataRows = used,
                    validPreviewRows = validRows,
                    invalidPreviewRows = rowErr.Count,
                    previewRows,
                    errors = rowErr
                });
            }

            if (rowErr.Count > 0)
            {
                return BadRequest(new
                {
                    message =
                        "No se puede crear solicitudes hasta que todas las filas del Excel sean válidas. Corrija las filas con error y cargue una versión nueva; use la vista previa para revisar antes de confirmar.",
                    scannedDataRows = used,
                    inserted = 0,
                    failedRows = rowErr.Count,
                    errors = rowErr
                });
            }

            inserted = 0;
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var p in pendingAdds)
                {
                    var req = new Request
                    {
                        Title = p.TitEs,
                        TitleEn = p.TitEn,
                        Description = p.Html,
                        DueDate = p.DueUtc,
                        Priority = p.Prio,
                        Status = ImportStatusDraft,
                        ProjectId = projectId,
                        RequestNumber = await RequestNumberAllocator.AllocateNextAsync(
                            _context, projectId, cancellationToken),
                        FirmContactId = p.FirmContactId,
                        UserCreatedId = actorId.Value,
                        CreatedAt = DateTime.UtcNow,
                        RequestGuid = Guid.NewGuid(),
                        PortalHandoffStatus = (int)RequestPortalHandoff.WithClient
                    };
                    _context.Requests.Add(req);
                    await _context.SaveChangesAsync(cancellationToken);
                    RequestHistoryLog.Add(_context, req.RequestId, actorId.Value, RequestHistoryLog.Created,
                        $"Importación Excel: «{req.Title}» / «{req.TitleEn}»");
                    await _context.SaveChangesAsync(cancellationToken);
                    inserted++;
                    insertedRequestIds.Add(req.RequestId);
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch (Exception exBulk)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogWarning(exBulk, "Importación Excel en bloque proyecto {Pid}", projectId);
                return BadRequest(new
                {
                    message =
                        "La base de datos rechazó la importación masiva (longitudes máximas, restricciones, etc.). No se creó ninguna solicitud.",
                    scannedDataRows = used,
                    inserted = 0,
                    failedRows = pendingAdds.Count,
                    errors = new object[]
                    {
                        new { row = (int?)null, message = "Base de datos: " + (exBulk.InnerException?.Message ?? exBulk.Message) }
                    }
                });
            }

            return Ok(new
            {
                scannedDataRows = used,
                inserted,
                failedRows = 0,
                errors = Array.Empty<object>(),
                insertedRequestIds
            });
        }
    }

    private static string TruncateUi(string raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private async Task<object> BuildPrerequisiteSnapshotAsync(int firmId)
    {
        var nUsers = await TenantQueries.ActiveFirmStaffInFirm(_context, firmId)
            .Where(u => u.Email != null && u.Email != "")
            .CountAsync();

        var nPt = await _context.ProjectTypes.AsNoTracking().CountAsync(p => p.FirmId == firmId);

        var nCust = await _context.Customers.AsNoTracking().CountAsync(c => c.FirmId == firmId);

        var blockers = new List<string>();
        if (nUsers == 0)
            blockers.Add("NO_INTERNAL_USERS_FOR_FIRM_CONTACT");

        List<object> hints = [];
        if (nPt == 0)
            hints.Add(new { code = "PROJECT_TYPES_CATALOG_EMPTY" });
        if (nCust == 0)
            hints.Add(new { code = "NO_CUSTOMERS" });

        return new { blockerCodes = blockers, hints };
    }

    private async Task<List<string>> GetBlockersAsync(int firmId, CancellationToken ct)
    {
        var hasContactUsers = await TenantQueries.ActiveFirmStaffInFirm(_context, firmId)
            .Where(u => u.Email != null && u.Email != "")
            .AnyAsync(ct);
        List<string> b = [];
        if (!hasContactUsers)
            b.Add("NO_INTERNAL_USERS_FOR_FIRM_CONTACT");
        return b;
    }

    private static bool TryLocateContiguousHeaders(
        IXLWorksheet ws,
        IReadOnlyList<string> expect,
        out int headerRowIndex,
        out int firstColNumber,
        out string error)
    {
        headerRowIndex = -1;
        firstColNumber = -1;
        error = "";
        var want = expect.Select(FieldKeyFold).ToList();

        var maxR = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 20);
        var maxC = ws.LastColumnUsed()?.ColumnNumber() ?? 20;
        maxC = Math.Min(maxC, 32);

        for (var rr = 1; rr <= maxR; rr++)
        {
            for (var cc = 1; cc <= maxC; cc++)
            {
                if (!string.Equals(FieldKeyFold(NormalizeCellText(ws.Cell(rr, cc))),
                        want[0],
                        StringComparison.Ordinal))
                    continue;

                var match = true;
                for (var k = 0; k < want.Count && match; k++)
                {
                    if (!string.Equals(FieldKeyFold(NormalizeCellText(ws.Cell(rr, cc + k))),
                            want[k],
                            StringComparison.Ordinal))
                        match = false;
                }
                if (!match)
                    continue;
                headerRowIndex = rr;
                firstColNumber = cc;
                return true;
            }
        }

        error =
            "No se encontraron columnas típicas. Use la pestaña Solicitudes conservando Titulo_es, Titulo_en, … en fila inicial.";
        return false;
    }

    private static string FieldKeyFold(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var trimmed = Regex.Replace(raw.Trim().ToLowerInvariant(), "\\s+", "_");
        trimmed = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsWhiteSpace(ch) ? '_' : ch);
        }

        trimmed = Regex.Replace(sb.ToString(), "\\s+", "_");
        trimmed = Regex.Replace(trimmed.ToLowerInvariant(), "[^a-z0-9_]", "_");
        return Regex.Replace(trimmed, "_+", "_").Trim('_');
    }

    private static string NormalizeCellText(IXLCell c)
    {
        try
        {
            return c.GetString().Trim();
        }
        catch
        {
            try { return (c.Value.ToString() ?? "").Trim(); }
            catch { return ""; }
        }
    }

    private static bool IsRowEmpty(IXLWorksheet ws, int r, int lastColScan)
    {
        for (var c = 1; c <= lastColScan && c <= 40; c++)
            if (!string.IsNullOrWhiteSpace(NormalizeCellText(ws.Cell(r, c))))
                return false;
        return true;
    }

    private static MemoryStream GenerateTemplateExcel(
        string projectCode,
        string projectName,
        int pid,
        IReadOnlyList<string> hdrs)
    {
        using var workbook = new XLWorkbook();

        var data = workbook.Worksheets.Add("Solicitudes");
        for (var i = 0; i < hdrs.Count; i++)
            data.Cell(1, i + 1).Value = hdrs[i];
        data.Row(1).Style.Font.Bold = true;
        data.Cell(2, 1).Value = "(Ejemplo) Documentación soporte ciclo FY";
        data.Cell(2, 2).Value = "(Example) Support documentation FY cycle";
        data.Cell(2, 3).Value = TemplateFirmContactEmailExample;
        data.Cell(2, 4).Value =
            "Ejemplo texto. Borrar la fila 2 antes de importar registros.";
        data.Cell(2, 5).Clear();
        data.Cell(2, 6).Value = "1";
        data.SheetView.FreezeRows(1);

        var h = workbook.Worksheets.Add("Ayuda");
        h.Cell(1, 1).Value = $"Proyecto {pid}: {projectCode} — {projectName}";
        h.Cell(3, 1).Value =
            "Titulo_es / Titulo_en: obligatorios, máx 250 caracteres.";
        h.Cell(4, 1).Value =
            "Email_contacto_firma = usuario interno ACTIVO con membresía en ESTA firma (la del tenant actual), correo EXACTAMENTE igual al alta en AMPERA; no portal cliente.";
        h.Cell(5, 1).Value =
            $"La fila 2 muestra «{TemplateFirmContactEmailExample}» solo como muestra ficticia — bórrela o reemplácela por un correo real de su equipo en esta firma; si el usuario pertenece a otra firma aunque comparta correo, la fila SE RECHAZA.";
        h.Cell(6, 1).Value = "Prioridad 0=Baja … 3=Crítica; vacío=1.";
        h.Cell(7, 1).Value =
            "Todas las solicitudes importadas se crean en estado Borrador; cámbielo después en la aplicación.";
        h.Cell(8, 1).Value =
            "Si no puede cargar porque faltan tablas auxiliares, cargue antes Tipos de proyecto o Clientes (ver avisos de la aplicación).";
        h.Columns().Width = 120;

        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static bool TryParsePri(string txt, out int p, out string err)
    {
        err = "";
        txt = txt.Trim();
        if (txt.Length == 0)
        {
            p = 1;
            return true;
        }

        if (int.TryParse(txt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            if (n is >= 0 and <= 3)
            {
                p = n;
                return true;
            }

            err = $"Prioridad {n} debe ser 0–3.";
            p = default;
            return false;
        }

        var f = FieldKeyFold(txt);
        p = int.MinValue;
        if (f.Contains("low", StringComparison.Ordinal) || f.Contains("baja", StringComparison.Ordinal)) p = 0;
        else if (f.Contains("high", StringComparison.Ordinal) || f.Contains("alta", StringComparison.Ordinal)) p = 2;
        else if (f.Contains("critica", StringComparison.Ordinal) || f.Contains("critico", StringComparison.Ordinal))
            p = 3;
        else if (string.IsNullOrEmpty(f))
            p = int.MinValue;
        else // media / medio
            p = 1;

        if (p >= 0)
            return true;
        err = "Prioridad desconocida.";
        return false;
    }

    private static bool TryParseDueDate(IXLCell cell, string text, out string err, out DateTime? utcIso)
    {
        err = "";
        utcIso = null;

        foreach (var format in new[]
                     {
                         "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "M/d/yyyy"
                     })
        {
            if (DateTime.TryParseExact(text.Trim(), format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local))
            {
                utcIso =
                    DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified).ToUniversalTime();
                return true;
            }
        }

        if (cell.TryGetValue(out DateTime native))
        {
            utcIso = DateTime.SpecifyKind(native.Date, DateTimeKind.Unspecified).ToUniversalTime();
            return true;
        }

        try
        {
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                var da = DateTime.FromOADate(cell.GetDouble());
                utcIso = DateTime.SpecifyKind(da.Date, DateTimeKind.Unspecified).ToUniversalTime();
                return true;
            }
        }
        catch { /* ignore */ }

        err = $"Fecha «{text}» ilegible.";
        return false;
    }

    private static bool TitlesOk(string titEs, string titEn, out string errMsg)
    {
        errMsg = "";
        if (string.IsNullOrWhiteSpace(titEs)) { errMsg = "Titulo_es obligatorio."; return false; }
        if (titEs.Trim().Length > 250)
        {
            errMsg = "Titulo_es > 250 caracteres.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(titEn)) { errMsg = "Titulo_en obligatorio."; return false; }
        if (titEn.Trim().Length > 250)
        {
            errMsg = "Titulo_en > 250 caracteres.";
            return false;
        }

        return true;
    }

    private static string PlainParagraphsHtml(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return "";
        var paragraphs = Regex.Split(plainText.Trim(), "\\r\\n|\\n|\\r");
        var sb = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var p = paragraph.Trim();
            if (p.Length == 0)
                continue;
            sb.Append("<p>").Append(WebUtility.HtmlEncode(p)).Append("</p>");
        }

        return sb.ToString();
    }

    private static string SanitizeFile(string code) =>
        Regex.Replace((code ?? "proyecto").Trim(), @"[^A-Za-z0-9._-]", "_");
}
