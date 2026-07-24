namespace AuditWorkflow.Models.Core;

/// <summary>Eventos internos de la firma sobre envío al portal del cliente.</summary>
public static class RequestPublishApprovalEventTypes
{
    public const int ApprovalRequested = 1;
    public const int ApprovedAndPublished = 2;
    public const int Rejected = 3;
    public const int Cancelled = 4;
    public const int PublishedDirect = 5;
}
