using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace AuditWorkflow.Services;

/// <summary>
/// Digest diario de vencidos: un correo agregado por destinatario (firma o cliente),
/// una sola vez al día. Depende de <see cref="OverdueWeeklyReminderPolicy"/> para elegibilidad.
/// </summary>
public class RequestOverdueWeeklyReminderService(
    AppDbContext context,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    IOptions<DueReminderJobSettings> jobOptions,
    ILogger<RequestOverdueWeeklyReminderService> logger) : IRequestOverdueWeeklyReminderService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly DueReminderJobSettings _jobSettings = jobOptions.Value ?? new DueReminderJobSettings();
    private readonly ILogger<RequestOverdueWeeklyReminderService> _logger = logger;

    public async Task<OverdueWeeklyProcessResult> ProcessOverdueWeeklyRemindersAsync(
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        var today = asOfDate ?? DateOnly.FromDateTime(ServerClock.Now);
        var todayStart = today.ToDateTime(TimeOnly.MinValue);

        if (!_jobSettings.OverdueWeeklyEnabled)
        {
            _logger.LogInformation("Digest diario de vencidos deshabilitado (OverdueWeeklyEnabled=false).");
            return new OverdueWeeklyProcessResult { ProcessedForDate = today };
        }

        var candidates = await _context.Requests
            .AsNoTracking()
            .Include(r => r.Project)!.ThenInclude(p => p!.Customer)
            .Where(r => r.ClosedAt == null)
            .Where(r => r.Status != OverdueWeeklyReminderPolicy.DraftStatus)
            .Where(r => r.DueDate != null)
            .ToListAsync(cancellationToken);

        var overdue = candidates
            .Where(r => OverdueWeeklyReminderPolicy.IsOverdueOpenRequest(r, today))
            .ToList();

        if (overdue.Count == 0)
        {
            return new OverdueWeeklyProcessResult
            {
                ProcessedForDate = today,
                OverdueRequestsFound = 0
            };
        }

        // Destinatario → solicitudes (sin duplicar).
        var buckets = new Dictionary<int, Dictionary<int, Request>>();

        foreach (var request in overdue)
        {
            var project = request.Project;
            if (project == null)
                continue;

            var firmId = project.FirmId;

            if (_jobSettings.OverdueNotifyFirmStaff)
            {
                foreach (var uid in await RequestNotificationRecipients.GetFirmStaffUserIdsAsync(
                             _context, firmId, request.RequestId, project.ProjectId, request.FirmContactId, cancellationToken))
                    AddToBucket(buckets, uid, request);
            }

            if (_jobSettings.OverdueNotifyClientPortal && project.CustomerId > 0)
            {
                foreach (var uid in await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
                             _context, firmId, project.CustomerId, request.RequestId, cancellationToken))
                    AddToBucket(buckets, uid, request);
            }
        }

        var emailsSent = 0;
        var skippedAlready = 0;
        var skippedNoEmail = 0;

        foreach (var (userId, byRequestId) in buckets)
        {
            var already = await _context.RequestOverdueReminderDispatches.AsNoTracking()
                .AnyAsync(
                    d => d.UserId == userId && d.NotifyOnDate == todayStart,
                    cancellationToken);
            if (already)
            {
                skippedAlready++;
                continue;
            }

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive, cancellationToken);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                skippedNoEmail++;
                // Marcar el día para no reintentar en bucle si el usuario no tiene correo.
                await RecordDigestDispatchAsync(userId, todayStart, cancellationToken);
                continue;
            }

            var reqs = byRequestId.Values
                .OrderBy(r => r.DueDate)
                .ThenBy(r => r.RequestNumber)
                .ToList();

            var isClient = user.UserType == TenantQueries.UserTypeClient;
            await SendDigestEmailAsync(user.Email.Trim(), reqs, today, isClient, cancellationToken);

            // Una notificación in-app resumen (no N notificaciones).
            var firmId = reqs[0].Project!.FirmId;
            var summaryBody = reqs.Count == 1
                ? $"Hay 1 solicitud vencida pendiente: «{FormatTitlesPlain(reqs[0].Title, reqs[0].TitleEn)}»."
                : $"Hay {reqs.Count} solicitudes vencidas pendientes de atención.";
            var appLink = isClient ? "/portal/requests" : "/requests";
            await UserNotificationCommands.CreateManyAsync(
                _context,
                firmId,
                [userId],
                "Solicitudes vencidas",
                summaryBody,
                appLink,
                reqs[0].RequestId,
                NotificationKind.OverdueWeeklyReminder,
                cancellationToken);

            await RecordDigestDispatchAsync(userId, todayStart, cancellationToken);
            emailsSent++;
        }

        return new OverdueWeeklyProcessResult
        {
            ProcessedForDate = today,
            OverdueRequestsFound = overdue.Count,
            DigestEmailsSent = emailsSent,
            RecipientsSkippedAlreadySentToday = skippedAlready,
            RecipientsSkippedNoEmail = skippedNoEmail
        };
    }

    private static void AddToBucket(
        Dictionary<int, Dictionary<int, Request>> buckets,
        int userId,
        Request request)
    {
        if (!buckets.TryGetValue(userId, out var byReq))
        {
            byReq = [];
            buckets[userId] = byReq;
        }

        byReq[request.RequestId] = request;
    }

    private async Task RecordDigestDispatchAsync(
        int userId,
        DateTime todayStart,
        CancellationToken cancellationToken)
    {
        _context.RequestOverdueReminderDispatches.Add(new RequestOverdueReminderDispatch
        {
            UserId = userId,
            NotifyOnDate = todayStart,
            DispatchedAt = ServerClock.Now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task SendDigestEmailAsync(
        string email,
        IReadOnlyList<Request> reqs,
        DateOnly today,
        bool isClientPortal,
        CancellationToken cancellationToken)
    {
        var tableRows = new List<string[]>(reqs.Count);
        foreach (var r in reqs)
        {
            var dueDay = DateOnly.FromDateTime(r.DueDate!.Value.Date);
            var days = OverdueWeeklyReminderPolicy.DaysOverdue(r, today);
            var plain = FormatTitlesPlain(r.Title, r.TitleEn);
            var titleEsc = WebUtility.HtmlEncode(plain);
            var href = isClientPortal
                ? HandoffLinkForClient(r.RequestGuid)
                : HandoffLinkForFirm(r.RequestGuid);
            var safeHref = WebUtility.HtmlEncode(href);
            var customerLabel = WebUtility.HtmlEncode((r.Project?.Customer?.Name ?? "—").Trim());
            var dueLabel = WebUtility.HtmlEncode($"{dueDay:dd/MM/yyyy} ({days}d)");

            var idCell =
                $$"""<span style="font-weight:700;color:{{AmperaEmailPalette.TealOscuro}};">{{WebUtility.HtmlEncode(r.RequestNumber.ToString())}}</span>""";
            var titleCell =
                $"<a href=\"{safeHref}\" style=\"color:{AmperaEmailPalette.TealOscuro};font-weight:600;text-decoration:none;border-bottom:1px solid {AmperaEmailPalette.TealClaro};\">{titleEsc}</a>";

            tableRows.Add([idCell, customerLabel, dueLabel, titleCell]);
        }

        var tableHtml = AmperaBrandedEmail.BuildBulkRequestDataTableHtml(
            ["#", "Cliente", "Vencimiento", "Solicitud"],
            tableRows);

        var intro = reqs.Count == 1
            ? AmperaBrandedEmail.BodyParagraph(
                "Hay <strong>1</strong> solicitud con fecha de vencimiento ya pasada. Detalle:")
            : AmperaBrandedEmail.BodyParagraph(
                $"Hay <strong>{reqs.Count}</strong> solicitudes con fecha de vencimiento ya pasada. Resumen del día:");

        var bodyInnerHtml = $"{intro}\n{tableHtml}";
        var cardHeadline = reqs.Count == 1
            ? "Solicitud vencida"
            : $"{reqs.Count} solicitudes vencidas";
        var subject = reqs.Count == 1
            ? $"Solicitud vencida: {FormatTitlesPlain(reqs[0].Title, reqs[0].TitleEn)}"
            : $"AMPERA: {reqs.Count} solicitudes vencidas";

        var listLink = isClientPortal
            ? PortalListLink(client: true)
            : PortalListLink(client: false);

        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Solicitudes vencidas — AMPERA",
            headerSubtitle: isClientPortal ? "Portal del cliente" : "Portal de la firma",
            cardHeadline: cardHeadline,
            bodyInnerHtml: bodyInnerHtml,
            primaryButtonHref: listLink,
            primaryButtonText: isClientPortal ? "Abrir solicitudes del portal" : "Abrir listado de solicitudes",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(listLink));

        try
        {
            await _emailService.SendHtmlAsync(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { email },
                subject,
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar digest diario de vencidos a {Email}.", email);
        }
    }

    private static string FormatTitlesPlain(string? title, string? titleEn)
    {
        var es = (title ?? "").Trim();
        var en = (titleEn ?? "").Trim();
        if (string.IsNullOrEmpty(es)) return en;
        if (string.IsNullOrEmpty(en)) return es;
        return $"{es} / {en}";
    }

    private string PortalListLink(bool client)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return client ? "/portal/requests" : "/requests";
        return client ? $"{baseUrl}/portal/requests" : $"{baseUrl}/requests";
    }

    private string HandoffLinkForFirm(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/requests/{requestGuid:D}";
    }

    private string HandoffLinkForClient(Guid requestGuid)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl) ? "#" : $"{baseUrl}/portal/requests/{requestGuid:D}";
    }
}
