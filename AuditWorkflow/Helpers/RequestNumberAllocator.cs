using AuditWorkflow.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Helpers;

public static class RequestNumberAllocator
{
    /// <summary>
    /// Asigna el siguiente RequestNumber dentro del proyecto (1, 2, 3…).
    /// Ejecutar dentro de la misma transacción que el INSERT de la solicitud.
    /// </summary>
    public static async Task<int> AllocateNextAsync(
        AppDbContext context,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var max = await context.Requests
            .Where(r => r.ProjectId == projectId)
            .MaxAsync(r => (int?)r.RequestNumber, cancellationToken);
        return (max ?? 0) + 1;
    }
}
