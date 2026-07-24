namespace AuditWorkflow.Models.Core;

/// <summary>
/// Solicitud borrador pendiente de aprobación por manager/socio antes de publicar en portal cliente.
/// </summary>
public static class RequestPublishToClientApproval
{
    public const int None = 0;
    public const int PendingManagerPartner = 1;
    /// <summary>Manager/socio rechazó el envío; el solicitante debe corregir y volver a tramitar.</summary>
    public const int RejectedByManagerPartner = 2;
}
