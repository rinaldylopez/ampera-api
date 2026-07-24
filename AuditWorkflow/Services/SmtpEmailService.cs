using AuditWorkflow.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AuditWorkflow.Services;

public class SmtpEmailService(ILogger<SmtpEmailService> logger, IOptions<EmailSettings> options) : IEmailService
{
    private readonly EmailSettings _settings = options.Value ?? new EmailSettings();

    public async Task SendHtmlAsync(
        IEnumerable<string> toAddresses,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var list = toAddresses
            .Where(static a => !string.IsNullOrWhiteSpace(a))
            .Select(static a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
        {
            logger.LogWarning("Correo omitido: sin destinatarios. Asunto: {Subject}", subject);
            return;
        }

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.SmtpHost))
        {
            logger.LogInformation(
                "Correo no enviado (SMTP deshabilitado o sin host). Destinatarios: {To}. Asunto: {Subject}",
                string.Join(", ", list),
                subject);
            return;
        }

        var fromAddr = string.IsNullOrWhiteSpace(_settings.FromAddress) ? "noreply@localhost" : _settings.FromAddress.Trim();
        var fromName = string.IsNullOrWhiteSpace(_settings.FromName) ? "AMPERA" : _settings.FromName.Trim();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddr));
        foreach (var to in list)
            message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject ?? "";
        message.Body = new BodyBuilder { HtmlBody = htmlBody ?? "" }.ToMessageBody();

        using var client = new SmtpClient();
        var port = _settings.SmtpPort > 0 ? _settings.SmtpPort : 587;
        var socketOptions = port == 465
            ? SecureSocketOptions.SslOnConnect
            : port == 587
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(_settings.SmtpHost.Trim(), port, socketOptions, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_settings.User))
            await client.AuthenticateAsync(_settings.User, _settings.Password ?? "", cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("Correo enviado. Asunto: {Subject}", subject);
    }
}
