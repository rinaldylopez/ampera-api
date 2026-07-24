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
    IOptions<DueReminderJobSettings> options,
    ILogger<DueReminderJobsController> logger) : ControllerBase
{
    private readonly IRequestDueReminderService _reminderService = reminderService;
    private readonly DueReminderJobSettings _settings = options.Value ?? new DueReminderJobSettings();
    private readonly ILogger<DueReminderJobsController> _logger = logger;

    /// <summary>
    /// Ejecuta manualmente el procesamiento de recordatorios (scheduler externo o pruebas).
    /// Si <see cref="DueReminderJobSettings.JobApiKey"/> está configurada, envíe header X-Job-Key.
    /// Si no hay clave configurada, requiere usuario autenticado.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<DueReminderProcessResult>> Run(
        [FromHeader(Name = "X-Job-Key")] string? jobKey,
        CancellationToken cancellationToken)
    {
        if (!IsJobAuthorized(jobKey))
            return Unauthorized();

        var result = await _reminderService.ProcessDueRemindersAsync(null, cancellationToken);
        _logger.LogInformation(
            "Due reminders run manual: date={Date}, sent={Sent}, evaluated={Evaluated}.",
            result.ProcessedForDate,
            result.NotificationsSent,
            result.RulesEvaluated);
        return Ok(result);
    }

    private bool IsJobAuthorized(string? jobKey)
    {
        var configuredKey = (_settings.JobApiKey ?? "").Trim();
        if (!string.IsNullOrEmpty(configuredKey))
            return string.Equals(jobKey?.Trim(), configuredKey, StringComparison.Ordinal);
        return User.Identity?.IsAuthenticated == true;
    }
}
