namespace AuditWorkflow.Models.Global;

/// <summary>
/// Fila de la relación perfil ↔ ítem de menú, con datos del ítem para la UI.
/// </summary>
public class ProfileItemRowDto
{
    public int ProfileItemId { get; set; }
    public int ProfileId { get; set; }
    public int NavigationItemId { get; set; }
    public string NavigationName { get; set; } = "";
    public string Path { get; set; } = "";
    public string ModuleName { get; set; } = "";
}

public class NavigationItemCatalogDto
{
    public int NavigationItemId { get; set; }
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public class ProfileItemLinkInput
{
    public int NavigationItemId { get; set; }
}

/// <summary>
/// Sincroniza los accesos del perfil con la lista indicada (altas y bajas en una sola operación).
/// </summary>
public class ProfileItemsSyncInput
{
    public List<int> NavigationItemIds { get; set; } = [];
}
