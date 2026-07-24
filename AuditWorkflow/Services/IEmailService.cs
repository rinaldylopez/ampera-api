namespace AuditWorkflow.Services;

public interface IEmailService
{
    /// <summary>Envía un correo HTML. Omite destinatarios vacíos o duplicados.</summary>
    Task SendHtmlAsync(IEnumerable<string> toAddresses, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
