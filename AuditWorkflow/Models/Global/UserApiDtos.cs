namespace AuditWorkflow.Models.Global;

public class UserListDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public int ProfileId { get; set; }
    public string ProfileName { get; set; }
    public string SecurityRole { get; set; }
    public int UserType { get; set; }
    public int FirmId { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; }
    public int JobTitleId { get; set; }
    public string JobTitleName { get; set; }
    public string UserGuid { get; set; }
    public string? ProfilePhotoPath { get; set; }

    /// <summary>Usuarios internos: firmas con membresía. Cliente: solo la firma vinculada.</summary>
    public List<int> ManagedFirmIds { get; set; } = [];

    /// <summary>Usuarios portal: empresas con membresía (además de <see cref="CustomerId"/> como predeterminada).</summary>
    public List<int> ManagedCustomerIds { get; set; } = [];
}

public class UserCreateDto
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int ProfileId { get; set; }
    public string SecurityRole { get; set; } = "Standard";
    public int UserType { get; set; } = 1;
    public int? CustomerId { get; set; }
    public int JobTitleId { get; set; }

    /// <summary>Firmas adicionales; la firma hogar es la de su sesión actual.</summary>
    public List<int>? FirmIds { get; set; }

    /// <summary>Clientes adicionales; el predeterminado es <see cref="CustomerId"/>.</summary>
    public List<int>? CustomerIds { get; set; }
}

public class UserUpdateDto
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProfileId { get; set; }
    public string SecurityRole { get; set; } = "Standard";
    public int UserType { get; set; } = 1;
    public int? CustomerId { get; set; }
    public int JobTitleId { get; set; }

    /// <summary>Si se envía, reemplaza membresías para usuarios tipo firma.</summary>
    public List<int>? FirmIds { get; set; }

    /// <summary>Si se envía, reemplaza membresías adicionales para usuarios portal cliente.</summary>
    public List<int>? CustomerIds { get; set; }
}

public class UserPasswordUpdateDto
{
    public string Password { get; set; } = "";
}

public class UserFormOptionsDto
{
    public IReadOnlyList<IdNameDto> Profiles { get; set; } = [];
    public IReadOnlyList<IdNameDto> JobTitles { get; set; } = [];

    /// <summary>Códigos de <see cref="User.SecurityRole"/> permitidos al crear/editar usuarios.</summary>
    public IReadOnlyList<string> SecurityRoles { get; set; } = [];

    /// <summary>Firmas que el usuario actual puede asignar al crear/editar usuarios internos.</summary>
    public IReadOnlyList<IdNameDto> Firms { get; set; } = [];

    /// <summary>Clientes de la firma activa (asignar usuarios portal).</summary>
    public IReadOnlyList<IdNameDto> Customers { get; set; } = [];
}

public class IdNameDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
