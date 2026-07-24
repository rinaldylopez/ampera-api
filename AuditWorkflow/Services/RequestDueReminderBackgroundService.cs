using AuditWorkflow.Settings;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Services;

/// <summary>Ejecuta el servicio de recordatorios de vencimiento una vez al día (UTC).</summary>
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
            "Due reminder background job activo. Hora UTC objetivo: {Hour}, intervalo: {Interval}h.",
            runHour,
            intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var today = DateOnly.FromDateTime(now);
                if (now.Hour >= runHour && _lastRunDate != today)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IRequestDueReminderService>();
                    var result = await service.ProcessDueRemindersAsync(today, stoppingToken);
                    _lastRunDate = today;
                    _logger.LogInformation(
                        "Due reminders procesados para {Date}: evaluadas={Evaluated}, enviadas={Sent}, fuera ventana={Outside}, ya enviadas={Dup}.",
                        result.ProcessedForDate,
                        result.RulesEvaluated,
                        result.NotificationsSent,
                        result.SkippedOutsideWindow,
                        result.SkippedAlreadyDispatched);
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
