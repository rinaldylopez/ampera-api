namespace AuditWorkflow.Helpers;

/// <summary>
/// Reloj de negocio de Ampera: <see cref="DateTime.Now"/> del servidor donde corre la API.
/// Las fechas que ve el usuario (solicitudes, historial, adjuntos, etc.) deben usar esto,
/// no <see cref="DateTime.UtcNow"/>.
/// <para>
/// Excepción: JWT / claim <c>exp</c> y protocolos que exigen UTC pueden seguir en UTC.
/// </para>
/// </summary>
public static class ServerClock
{
    public static DateTime Now => DateTime.Now;
}
