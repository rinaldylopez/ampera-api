using AuditWorkflow.Data;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Helpers;

namespace AuditWorkflow.Helpers;

public static class RequestHistoryLog
{
    public const string Created = "Solicitud creada";
    public const string Updated = "Solicitud actualizada";
    public const string Assignment = "Asignación";
    public const string Comment = "Comunicación";
    public const string ClientFile = "Archivo del cliente";
    public const string FirmFile = "Archivo de la firma";
    public const string Flow = "Flujo portal";
    public const string Closed = "Cierre";
    public const string ChangeRequest = "Solicitud de cambio";

    public static void Add(
        AppDbContext db,
        int requestId,
        int userId,
        string statusAtMoment,
        string observation,
        bool firmInternalOnly = false)
    {
        db.RequestHistory.Add(new RequestHistory
        {
            RequestId = requestId,
            ActionById = userId,
            ActionDate = ServerClock.Now,
            StatusAtMoment = statusAtMoment,
            Observation = observation,
            FirmInternalOnly = firmInternalOnly
        });
    }
}
