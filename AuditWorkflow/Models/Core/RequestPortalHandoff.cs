namespace AuditWorkflow.Models.Core;

/// <summary>
/// Flujo portal: quién tiene el turno de la documentación PBC.
/// </summary>
public enum RequestPortalHandoff
{
    /// <summary>El cliente puede subir documentación y enviar a la firma.</summary>
    WithClient = 1,

    /// <summary>Enviada a la firma; la firma revisa adjuntos y puede devolver al cliente.</summary>
    WithFirm = 2,

    /// <summary>La firma devolvió la solicitud al cliente (correo al cliente).</summary>
    ReturnedToClient = 3
}
