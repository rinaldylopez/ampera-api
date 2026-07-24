using AuditWorkflow.Models.Core;

namespace AuditWorkflow.Helpers;

/// <summary>
/// Tipos estándar al crear una firma nueva o cuando se inicializa una firma sin catálogo.
/// </summary>
public static class DefaultProjectTypesForFirm
{
    public static IEnumerable<ProjectType> CreateSeedRows(int firmId) =>
    [
        new ProjectType
        {
            FirmId = firmId,
            Code = "Financial",
            Name = "Financial / Financiera",
            SortOrder = 10,
            IsActive = true
        },
        new ProjectType
        {
            FirmId = firmId,
            Code = "Fiscal",
            Name = "Fiscal / Fiscal",
            SortOrder = 20,
            IsActive = true
        },
        new ProjectType
        {
            FirmId = firmId,
            Code = "AML",
            Name = "AML / PLD",
            SortOrder = 30,
            IsActive = true
        },
        new ProjectType
        {
            FirmId = firmId,
            Code = "Other",
            Name = "Other / Otra",
            SortOrder = 40,
            IsActive = true
        }
    ];
}
