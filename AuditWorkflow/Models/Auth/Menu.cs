using System.Text.Json.Serialization;

namespace AuditWorkflow.Models.Auth
{
    /// <summary>
    /// Un módulo del menú lateral: agrupa los accesos (<see cref="Children"/>) asignados al perfil.
    /// </summary>
    public class Menu
    {
        public int ModuleId { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public string To { get; set; }

        [JsonPropertyName("items")]
        public List<Children> Children { get; set; }
    }

    /// <summary>
    /// Acceso / ítem de navegación dentro de un módulo.
    /// </summary>
    public class Children
    {
        public int NavigationItemId { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public string To { get; set; }
    }
}
