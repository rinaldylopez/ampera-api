namespace AuditWorkflow.Models.Core;

/// <summary>Tipo de cambio que el cliente solicita sobre una solicitud ya publicada.</summary>
public static class RequestChangeRequestKinds
{
    public const int DueDateExtension = 1;
    public const int ContentModification = 2;
    public const int Both = 3;

    public static bool IsValid(int kind) =>
        kind is DueDateExtension or ContentModification or Both;

    public static bool RequiresProposedDueDate(int kind) =>
        kind is DueDateExtension or Both;
}
