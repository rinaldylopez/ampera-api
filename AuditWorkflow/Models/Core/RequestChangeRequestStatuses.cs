namespace AuditWorkflow.Models.Core;

/// <summary>Estado de tramitación de una solicitud de cambio del portal cliente.</summary>
public static class RequestChangeRequestStatuses
{
    public const int Pending = 0;
    public const int Approved = 1;
    public const int Rejected = 2;
    public const int CancelledByClient = 3;

    public static bool IsTerminal(int status) =>
        status is Approved or Rejected or CancelledByClient;
}
