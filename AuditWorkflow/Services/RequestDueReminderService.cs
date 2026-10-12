using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace AuditWorkflow.Services;

public class RequestDueReminderService(
    AppDbContext context,
    IEmailService emailService,
    IOptions<EmailSettings> emailOptions,
    ILogger<RequestDueReminderService> logger) : IRequestDueReminderService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailOptions.Value ?? new EmailSettings();
    private readonly ILogger<RequestDueReminderService> _logger = logger;

    public async Task<DueReminderProcessResult> ProcessDueRemindersAsync(
        DateOnly? asOfDateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var today = asOfDateUtc ?? DateOnly.FromDateTime(ServerClock.Now);
        var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rules = await _context.RequestReminders
            .Include(r => r.Request)!.ThenInclude(req => req.Project)!.ThenInclude(p => p.Customer)
            .Where(r => r.IsActive)
            .Where(r => r.Request!.ClosedAt == null)
            .ToListAsync(cancellationToken);

        var evaluated = 0;
        var sent = 0;
        var skippedOutside = 0;
        var skippedDispatched = 0;
        var skippedNoDue = 0;

        foreach (var rule in rules)
        {
            evaluated++;
            var request = rule.Request;
            if (request == null)
            {
                skippedNoDue++;
                continue;
            }

            var dueDate = DateOnly.FromDateTime(rule.ReminderDueDate.Date);
            var windowStart = dueDate.AddDays(-rule.DaysBeforeDue);
            if (today < windowStart || today > dueDate)
            {
                skippedOutside++;
                continue;
            }

            var already = await _context.RequestReminderDispatches.AsNoTracking()
                .AnyAsync(
                    d => d.RequestReminderId == rule.RequestReminderId && d.NotifyOnDate == todayStart,
                    cancellationToken);
            if (already)
            {
                skippedDispatched++;
                continue;
            }

            var project = request.Project;
            if (project == null)
                continue;

            var firmId = project.FirmId;
            var titlePlain = FormatTitlesPlain(request.Title, request.TitleEn);
            var daysLeft = dueDate.DayNumber - today.DayNumber;
            var body = BuildNotificationBody(project.Customer?.Name, titlePlain, daysLeft, dueDate, rule.Note, isReminder: true);
            var firmLink = AppLinkPathForFirmRequest(request.RequestGuid);
            var clientLink = AppLinkPathForClientRequest(request.RequestGuid);

            var recipientIds = new HashSet<int>();
            if (rule.NotifyFirmStaff)
            {
                foreach (var uid in await RequestNotificationRecipients.GetFirmStaffUserIdsAsync(
                             _context, firmId, request.RequestId, project.ProjectId, request.FirmContactId, cancellationToken))
                    recipientIds.Add(uid);
            }

            if (rule.NotifyClientPortal && project.CustomerId > 0)
            {
                foreach (var uid in await RequestNotificationRecipients.GetClientPortalUserIdsAsync(
                             _context, firmId, project.CustomerId, request.RequestId, cancellationToken))
                    recipientIds.Add(uid);
            }

            if (recipientIds.Count == 0)
            {
                _logger.LogWarning(
                    "Recordatorio {ReminderId} solicitud {RequestId} sin destinatarios.",
                    rule.RequestReminderId,
                    request.RequestId);
            }
            else
            {
                var firmRecipients = new List<int>();
                var clientRecipients = new List<int>();
                foreach (var uid in recipientIds)
                {
                    var isClient = await _context.Users.AsNoTracking()
                        .AnyAsync(u => u.UserId == uid && u.UserType == TenantQueries.UserTypeClient, cancellationToken);
                    if (isClient) clientRecipients.Add(uid);
                    else firmRecipients.Add(uid);
                }

                if (firmRecipients.Count > 0)
                {
                    await UserNotificationCommands.CreateManyAsync(
                        _context,
                        firmId,
                        firmRecipients,
                        "Recordatorio de vencimiento",
                        body,
                        firmLink,
                        request.RequestId,
                        NotificationKind.DueDateReminder,
                        cancellationToken);
                    await TrySendReminderEmailsAsync(
                        firmRecipients,
                        titlePlain,
                        body,
                        HandoffLinkForFirm(request.RequestGuid),
                        cancellationToken);
                }

                if (clientRecipients.Count > 0)
                {
                    await UserNotificationCommands.CreateManyAsync(
                        _context,
                        firmId,
                        clientRecipients,
                        "Recordatorio de vencimiento",
                        body,
                        clientLink,
                        request.RequestId,
                        NotificationKind.DueDateReminder,
                        cancellationToken);
                    await TrySendReminderEmailsAsync(
                        clientRecipients,
                        titlePlain,
                        body,
                        HandoffLinkForClient(request.RequestGuid),
                        cancellationToken);
                }

                sent += recipientIds.Count;
            }

            _context.RequestReminderDispatches.Add(new RequestReminderDispatch
            {
                RequestReminderId = rule.RequestReminderId,
                NotifyOnDate = todayStart,
                DispatchedAtUtc = ServerClock.Now
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new DueReminderProcessResult
        {
            ProcessedForDate = today,
            RulesEvaluated = evaluated,
            NotificationsSent = sent,
            SkippedOutsideWindow = skippedOutside,
            SkippedAlreadyDispatched = skippedDispatched,
            SkippedMissingDueDate = skippedNoDue
        };
    }

    private async Task TrySendReminderEmailsAsync(
        IReadOnlyList<int> userIds,
        string titlePlain,
        string bodyPlain,
        string link,
        CancellationToken cancellationToken)
    {
        var emails = await RequestNotificationRecipients.GetEmailsForUserIdsAsync(_context, userIds, cancellationToken);
        if (emails.Count == 0)
            return;

        var titleEsc = WebUtility.HtmlEncode(titlePlain);
        var bodyEsc = WebUtility.HtmlEncode(bodyPlain);
        var html = AmperaBrandedEmail.BuildNotificationDocument(
            htmlPageTitle: "Recordatorio de vencimiento — AMPERA",
            headerSubtitle: "AMPERA",
            cardHeadline: "Recordatorio de vencimiento",
            bodyInnerHtml: AmperaBrandedEmail.BodyParagraph(
                $"<strong>{titleEsc}</strong><br />{bodyEsc}"),
            primaryButtonHref: link,
            primaryButtonText: "Abrir solicitud",
            fallbackLinkDisplayEncoded: WebUtility.HtmlEncode(link));

        try
        {
            await _emailService.SendHtmlAsync(
                emails.ToHashSet(StringComparer.OrdinalIgnoreCase),
                $"Recordatorio: {titlePlain}",
                html,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar correo de recordatorio de vencimiento.");
        }
    }

    private static string BuildNotificationBody(
        string? customerName,
        string titlePlain,
        int daysLeft,
        DateOnly dueDate,
        string? note,
        bool isReminder = false)
    {
        var cn = string.IsNullOrWhiteSpace(customerName) ? "" : customerName.Trim();
        var dueText = dueDate.ToString("dd/MM/yyyy");
        var daysText = daysLeft <= 0
            ? (isReminder ? "recordatorio vence hoy" : "vence hoy")
            : daysLeft == 1
                ? (isReminder ? "recordatorio vence mañana" : "vence mañana")
                : isReminder
                    ? $"recordatorio vence en {daysLeft} días ({dueText})"
                    : $"vence en {daysLeft} días ({dueText})";
        var baseText = string.IsNullOrEmpty(cn)
            ? $"«{titlePlain}» {daysText}."
            : $"{cn} · «{titlePlain}» {daysText}.";
        if (!string.IsNullOrWhiteSpace(note))
            baseText += $" Nota: {note.Trim()}";
        return baseText.Length <= 1000 ? baseText : baseText[..1000];
    }

    private static string FormatTitlesPlain(string? title, string? titleEn)
    {
        var es = (title ?? "").Trim();
        var en = (titleEn ?? "").Trim();
        if (string.IsNullOrEmpty(es)) return en;
        if (string.IsNullOrEmpty(en)) return es;
        return $"{es} / {en}";
    }

    private static string AppLinkPathForFirmRequest(Guid requestGuid) => $"/requests/{requestGuid:D}";

    private static string AppLinkPathForClientRequest(Guid requestGuid) => $"/portal/requests/{requestGuid:D}";

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
