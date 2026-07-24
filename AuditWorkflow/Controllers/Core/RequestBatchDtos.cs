using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Controllers.Core;

public sealed class RequestIdBatchDto
{
    /// <summary>Identificadores de solicitud únicos dentro de la firma.</summary>
    public List<int> RequestIds { get; set; } = [];
}

public sealed class RejectPublishApprovalDto
{
    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = "";
}

public sealed class RequestPublishApprovalRejectionBatchDto
{
    public List<RequestPublishApprovalRejectionItemDto> Items { get; set; } = [];
}

public sealed class RequestPublishApprovalRejectionItemDto
{
    public int RequestId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = "";
}

/// <summary>Resultado de operaciones masivas con éxito parcial.</summary>
public sealed class RequestBatchResultDto
{
    public List<int> Succeeded { get; init; } = [];

    public List<RequestBatchFailureDto> Failed { get; init; } = [];
}

public sealed class RequestBatchFailureDto
{
    public int RequestId { get; init; }
    public string Error { get; init; } = "";
}
