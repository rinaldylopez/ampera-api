namespace AuditWorkflow.Models.Global;

/// <summary>Recursos con permisos de firma configurables por perfil (C/U/D).</summary>
public enum ProfileProtectedResource : byte
{
    Teams = 1,
    Customers = 2,
    Projects = 3,
    Requests = 4
}
