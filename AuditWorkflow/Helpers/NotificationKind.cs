namespace AuditWorkflow.Helpers;

public static class NotificationKind
{
    public const byte PublishedToClient = 1;
    public const byte ClientSentToFirm = 2;
    public const byte FirmReturnedToClient = 3;

    /** Firma marca la solicitud como rechazada / cancelada en revisión PBC (notificación a portal cliente). */
    public const byte FirmDeclinedRequest = 4;

    /** Solicitud borrador esperando aprobación manager/socio para publicar en portal cliente. */
    public const byte PublishToClientApprovalPending = 5;

    /** Recordatorio de vencimiento de solicitud (due date). */
    public const byte DueDateReminder = 6;

    /** El cliente canceló el envío a revisión de la firma (notificación a personal de la firma). */
    public const byte ClientCancelledSendToFirm = 7;

    /** Recordatorio semanal: solicitud abierta con fecha de vencimiento ya pasada. */
    public const byte OverdueWeeklyReminder = 8;

    /** El cliente solicitó ampliación de plazo y/o modificación del requerimiento. */
    public const byte ClientChangeRequestPending = 9;

    /** La firma resolvió (aprobó/rechazó) una solicitud de cambio del cliente. */
    public const byte ClientChangeRequestResolved = 10;
}
