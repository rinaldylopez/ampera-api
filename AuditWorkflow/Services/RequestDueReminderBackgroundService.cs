using AuditWorkflow.Settings;
using Microsoft.Extensions.Options;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Services;

/// <summary>
/// Ejecuta una vez al día (hora del servidor):
/// 1) recordatorios pre-vencimiento (reglas),
/// 2) recordatorios semanales de solicitudes ya vencidas.
/// </summary>
public class RequestDueReminderBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<DueReminderJobSettings> options,
    ILogger<RequestDueReminderBackgroundService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly DueReminderJobSettings _settings = options.Value ?? new DueReminderJobSettings();
    private readonly ILogger<RequestDueReminderBackgroundService> _logger = logger;
    private DateOnly? _lastRunDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Due reminder background job está deshabilitado (DueReminderJob:Enabled=false).");
            return;
        }

        var intervalHours = _settings.CheckIntervalHours is > 0 and <= 24
            ? _settings.CheckIntervalHours
            : 1;
        var runHour = _settings.RunAtHourUtc is >= 0 and <= 23
            ? _settings.RunAtHourUtc
            : 12;

        _logger.LogInformation(
            "Due reminder background job activo. Hora del servidor objetivo: {Hour} (DueReminderJob:RunAtHourUtc), intervalo: {Interval}h.",
            runHour,
            intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = ServerClock.Now;
                var today = DateOnly.FromDateTime(now);
                if (now.Hour >= runHour && _lastRunDate != today)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dueService = scope.ServiceProvider.GetRequiredService<IRequestDueReminderService>();
                    var result = await dueService.ProcessDueRemindersAsync(today, stoppingToken);
                    _logger.LogInformation(
                        "Due reminders procesados para {Date}: evaluadas={Evaluated}, enviadas={Sent}, fuera ventana={Outside}, ya enviadas={Dup}.",
                        result.ProcessedForDate,
                        result.RulesEvaluated,
                        result.NotificationsSent,
                        result.SkippedOutsideWindow,
                        result.SkippedAlreadyDispatched);

                    var overdueService = scope.ServiceProvider.GetRequiredService<IRequestOverdueWeeklyReminderService>();
                    var overdue = await overdueService.ProcessOverdueWeeklyRemindersAsync(today, stoppingToken);
                    _logger.LogInformation(
                        "Overdue digest diario para {Date}: vencidas={Found}, correos={Sent}, yaEnviadosHoy={SkipAlready}, sinEmail={SkipEmail}.",
                        overdue.ProcessedForDate,
                        overdue.OverdueRequestsFound,
                        overdue.DigestEmailsSent,
                        overdue.RecipientsSkippedAlreadySentToday,
                        overdue.RecipientsSkippedNoEmail);

                    _lastRunDate = today;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error en due reminder background job.");
            }

            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }
}
