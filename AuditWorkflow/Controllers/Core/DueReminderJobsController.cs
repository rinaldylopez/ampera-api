using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Controllers.Core;

[ApiController]
[Route("api/jobs/due-reminders")]
public class DueReminderJobsController(
    IRequestDueReminderService reminderService,
    IRequestOverdueWeeklyReminderService overdueWeeklyReminderService,
    IOptions<DueReminderJobSettings> options,
    ILogger<DueReminderJobsController> logger) : ControllerBase
{
    private readonly IRequestDueReminderService _reminderService = reminderService;
    private readonly IRequestOverdueWeeklyReminderService _overdueWeeklyReminderService = overdueWeeklyReminderService;
    private readonly DueReminderJobSettings _settings = options.Value ?? new DueReminderJobSettings();
    private readonly ILogger<DueReminderJobsController> _logger = logger;

    /// <summary>
    /// Ejecuta manualmente recordatorios pre-vencimiento + semanales de vencidos.
    /// Si <see cref="DueReminderJobSettings.JobApiKey"/> está configurada, envíe header X-Job-Key.
    /// Si no hay clave configurada, requiere usuario autenticado.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<object>> Run(
        [FromHeader(Name = "X-Job-Key")] string? jobKey,
        CancellationToken cancellationToken)
    {
        if (!IsJobAuthorized(jobKey))
            return Unauthorized();

        var due = await _reminderService.ProcessDueRemindersAsync(null, cancellationToken);
        var overdue = await _overdueWeeklyReminderService.ProcessOverdueWeeklyRemindersAsync(null, cancellationToken);
        _logger.LogInformation(
            "Reminders run manual: dueSent={DueSent}, overdueDigestEmails={OverdueSent}, overdueFound={Found}, date={Date}.",
            due.NotificationsSent,
            overdue.DigestEmailsSent,
            overdue.OverdueRequestsFound,
            due.ProcessedForDate);
        return Ok(new { due, overdue });
    }

    private bool IsJobAuthorized(string? jobKey)
    {
        var configuredKey = (_settings.JobApiKey ?? "").Trim();
        if (!string.IsNullOrEmpty(configuredKey))
            return string.Equals(jobKey?.Trim(), configuredKey, StringComparison.Ordinal);
        return User.Identity?.IsAuthenticated == true;
    }
}
