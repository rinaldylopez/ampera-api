using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Controllers.Global;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class UsersController(
    AppDbContext context,
    IOptions<AppSettings> appSettings,
    IWebHostEnvironment env,
    IFileStorageService fileStorage,
    IEmailService emailService,
    IOptions<EmailSettings> emailSettings,
    IUserFirmAccessService firmAccess,
    IUserCustomerAccessService customerAccess,
    ILogger<UsersController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly IUserFirmAccessService _firmAccess = firmAccess;
    private readonly IUserCustomerAccessService _customerAccess = customerAccess;
    private readonly AppSettings _appSettings = appSettings.Value ?? new AppSettings();
    private readonly IWebHostEnvironment _env = env;
    private readonly IFileStorageService _fileStorage = fileStorage;
    private readonly IEmailService _emailService = emailService;
    private readonly EmailSettings _emailSettings = emailSettings.Value ?? new EmailSettings();
    private readonly ILogger<UsersController> _logger = logger;
    private const int ClientPortalProfileId = ClientPortalProfileHelper.ExpectedProfileId;

    private string PasswordSecretSuffix() =>
        Environment.GetEnvironmentVariable("Secret") ?? _appSettings.Secret ?? "";

    [HttpGet("form-options")]
    public async Task<ActionResult<UserFormOptionsDto>> GetFormOptions(CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        var userId = User.GetUserId();
        if (firmId is null || userId is null) return Unauthorized();

        // Admin de plataforma: puede asignar cualquier firma (como en GET /api/Firms). Resto: solo membresías.
        var firms = User.IsInRole(UserSecurityRoles.Admin)
            ? await _context.Firms
                .AsNoTracking()
                .OrderBy(f => f.Name)
                .Select(f => new IdNameDto { Id = f.FirmId, Name = f.Name })
                .ToListAsync(cancellationToken)
            : (await _firmAccess.GetAccessibleFirmsAsync(userId.Value, _context, cancellationToken)).ToList();

        var profiles = await _context.Profiles
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new IdNameDto { Id = p.ProfileId, Name = p.Name })
            .ToListAsync();

        var jobTitles = await _context.JobTitle
            .AsNoTracking()
            .OrderBy(j => j.Name)
            .Select(j => new IdNameDto { Id = j.JobTitleId, Name = j.Name })
            .ToListAsync();

        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.FirmId == firmId.Value)
            .OrderBy(c => c.Name)
            .Select(c => new IdNameDto { Id = c.CustomerId, Name = c.Name })
            .ToListAsync(cancellationToken);

        return Ok(new UserFormOptionsDto
        {
            Profiles = profiles,
            JobTitles = jobTitles,
            SecurityRoles = UserSecurityRoles.All.ToList(),
            Firms = firms,
            Customers = customers
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserListDto>>> GetUsers()
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var list = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new UserListDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                ProfileId = u.ProfileId,
                ProfileName = u.Profile.Name,
                SecurityRole = u.SecurityRole,
                UserType = u.UserType,
                FirmId = u.FirmId,
                CustomerId = u.CustomerId,
                CustomerName = u.Customer != null ? u.Customer.Name : "",
                JobTitleId = u.JobTitleId,
                JobTitleName = u.JobTitle.Name,
                UserGuid = u.UserGuid ?? "",
                ProfilePhotoPath = u.ProfilePhotoPath,
                ManagedFirmIds = u.UserType == TenantQueries.UserTypeFirm
                    ? u.UserFirmMemberships.Select(m => m.FirmId).ToList()
                    : new List<int> { u.FirmId },
                ManagedCustomerIds = u.UserType == TenantQueries.UserTypeClient
                    ? u.UserCustomerMemberships.Select(m => m.CustomerId).ToList()
                    : new List<int>()
            })
            .ToListAsync();

        foreach (var dto in list)
            NormalizeClientUserDto(dto);

        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserListDto>> GetUser(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var dto = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .AsNoTracking()
            .Where(u => u.UserId == id)
            .Select(u => new UserListDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                ProfileId = u.ProfileId,
                ProfileName = u.Profile.Name,
                SecurityRole = u.SecurityRole,
                UserType = u.UserType,
                FirmId = u.FirmId,
                CustomerId = u.CustomerId,
                CustomerName = u.Customer != null ? u.Customer.Name : "",
                JobTitleId = u.JobTitleId,
                JobTitleName = u.JobTitle.Name,
                UserGuid = u.UserGuid ?? "",
                ProfilePhotoPath = u.ProfilePhotoPath,
                ManagedFirmIds = u.UserType == TenantQueries.UserTypeFirm
                    ? u.UserFirmMemberships.Select(m => m.FirmId).ToList()
                    : new List<int> { u.FirmId },
                ManagedCustomerIds = u.UserType == TenantQueries.UserTypeClient
                    ? u.UserCustomerMemberships.Select(m => m.CustomerId).ToList()
                    : new List<int>()
            })
            .FirstOrDefaultAsync();

        if (dto != null)
            NormalizeClientUserDto(dto);

        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> PostUser([FromBody] UserCreateDto dto, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var validationError = await ValidateUserWriteAsync(dto.UserType, dto.CustomerId, firmId.Value);
        if (!string.IsNullOrEmpty(validationError)) return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("FullName and Email are required.");

        var plainPassword = dto.Password?.Trim() ?? "";
        if (string.IsNullOrEmpty(plainPassword))
            plainPassword = TemporaryPasswordGenerator.Generate();
        else if (plainPassword.Length < 6)
            return BadRequest("Password must be at least 6 characters when provided. Leave blank to auto-generate.");

        var emailNorm = dto.Email.Trim();
        if (await _context.Users.AnyAsync(u => u.FirmId == firmId && u.Email == emailNorm, cancellationToken))
            return Conflict("Email is already in use in this firm.");

        var effectiveProfileId = dto.UserType == TenantQueries.UserTypeClient
            ? await ClientPortalProfileHelper.EnsureProfileExistsAsync(_context, cancellationToken)
            : dto.ProfileId;
        if (dto.UserType != TenantQueries.UserTypeClient
            && !await _context.Profiles.AnyAsync(p => p.ProfileId == effectiveProfileId, cancellationToken))
            return BadRequest("Perfil inválido. Seleccione un perfil de la lista.");
        if (!await _context.JobTitle.AnyAsync(j => j.JobTitleId == dto.JobTitleId, cancellationToken))
            return BadRequest("Invalid JobTitleId.");

        var securityRole = dto.UserType == TenantQueries.UserTypeClient
            ? UserSecurityRoles.Standard
            : string.IsNullOrWhiteSpace(dto.SecurityRole)
                ? UserSecurityRoles.Standard
                : dto.SecurityRole.Trim();
        if (!UserSecurityRoles.IsValid(securityRole))
            return BadRequest("Invalid SecurityRole.");

        var hash = EncryptPassword.HashPassword(plainPassword, PasswordSecretSuffix());

        var entity = new User
        {
            FullName = dto.FullName.Trim(),
            Email = emailNorm,
            PasswordHash = hash,
            IsActive = dto.IsActive,
            ProfileId = effectiveProfileId,
            SecurityRole = securityRole,
            UserType = dto.UserType,
            FirmId = firmId.Value,
            CustomerId = dto.UserType == 1 ? null : dto.CustomerId,
            JobTitleId = dto.JobTitleId,
            UserGuid = Guid.NewGuid().ToString("D"),
            MustChangePassword = true
        };

        _context.Users.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        if (entity.UserType == TenantQueries.UserTypeFirm)
        {
            var adminId = User.GetUserId();
            if (adminId is null) return Unauthorized();
            var firmErr = await ValidateFirmIdsForAdminAsync(
                adminId.Value,
                firmId.Value,
                dto.FirmIds,
                User.IsInRole(UserSecurityRoles.Admin),
                cancellationToken);
            if (firmErr != null)
                return BadRequest(firmErr);
            await _firmAccess.SyncMembershipsForFirmUserAsync(entity.UserId, entity.FirmId, dto.FirmIds ?? [], _context, cancellationToken);
        }

        if (entity.UserType == TenantQueries.UserTypeClient && entity.CustomerId is int homeCustomerId)
        {
            var adminId = User.GetUserId();
            if (adminId is null) return Unauthorized();
            var custErr = await ValidateCustomerIdsForAdminAsync(firmId.Value, dto.CustomerIds, cancellationToken);
            if (custErr != null)
                return BadRequest(custErr);
            await _customerAccess.SyncMembershipsForClientUserAsync(entity.UserId, homeCustomerId, dto.CustomerIds ?? [], _context, cancellationToken);
        }

        var firmName = await _context.Firms.AsNoTracking()
            .Where(f => f.FirmId == firmId.Value)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        await TrySendWelcomeEmailAsync(entity.FullName, entity.Email, plainPassword, firmName, cancellationToken);

        var created = await GetUserDtoAsync(entity.UserId, firmId.Value);
        return CreatedAtAction(nameof(GetUser), new { id = entity.UserId }, created);
    }

    private async Task TrySendWelcomeEmailAsync(string fullName, string email, string plainPassword, string firmName, CancellationToken cancellationToken)
    {
        var baseUrl = (_emailSettings.PortalBaseUrl ?? "").Trim().TrimEnd('/');
        var loginUrl = string.IsNullOrEmpty(baseUrl) ? "#" : $"{baseUrl}/auth/login";
        if (string.IsNullOrEmpty(baseUrl))
            _logger.LogWarning("PortalBaseUrl no está configurado (Email:PortalBaseUrl); el enlace del correo de bienvenida puede ser inválido.");

        if (!_emailSettings.Enabled || string.IsNullOrWhiteSpace(_emailSettings.SmtpHost))
        {
            _logger.LogInformation("Correo de bienvenida no enviado (SMTP deshabilitado). Usuario: {Email}", email);
            return;
        }

        var html = WelcomeNewUserEmail.BuildHtml(fullName, plainPassword, loginUrl, firmName);
        const string subject = "AMPERA — Su cuenta y contraseña provisional";
        try
        {
            await _emailService.SendHtmlAsync(new[] { email }, subject, html, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar el correo de bienvenida a {Email}", email);
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> PutUser(int id, [FromBody] UserUpdateDto dto, CancellationToken cancellationToken)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .Include(u => u.UserFirmMemberships)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null) return NotFound();

        var validationError = await ValidateUserWriteAsync(dto.UserType, dto.CustomerId, firmId.Value);
        if (!string.IsNullOrEmpty(validationError)) return BadRequest(validationError);

        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest("FullName and Email are required.");

        var emailNorm = dto.Email.Trim();
        var selectedFirmIds = dto.UserType == TenantQueries.UserTypeFirm
            ? (dto.FirmIds ?? []).Where(fid => fid > 0).Distinct().ToList()
            : [];
        var effectiveHomeFirmId = entity.FirmId;
        if (dto.UserType == TenantQueries.UserTypeFirm)
        {
            if (selectedFirmIds.Count == 0)
                return BadRequest("Debe asignar al menos una firma al usuario.");
            effectiveHomeFirmId = selectedFirmIds.Contains(entity.FirmId)
                ? entity.FirmId
                : selectedFirmIds[0];
        }
        if (await _context.Users.AnyAsync(u => u.FirmId == effectiveHomeFirmId && u.Email == emailNorm && u.UserId != id))
            return Conflict("Email is already in use in this firm.");

        var effectiveProfileId = dto.UserType == TenantQueries.UserTypeClient
            ? await ClientPortalProfileHelper.EnsureProfileExistsAsync(_context, cancellationToken)
            : dto.ProfileId;
        if (dto.UserType != TenantQueries.UserTypeClient
            && !await _context.Profiles.AnyAsync(p => p.ProfileId == effectiveProfileId, cancellationToken))
            return BadRequest("Perfil inválido. Seleccione un perfil de la lista.");
        if (!await _context.JobTitle.AnyAsync(j => j.JobTitleId == dto.JobTitleId))
            return BadRequest("Invalid JobTitleId.");

        var securityRole = dto.UserType == TenantQueries.UserTypeClient
            ? UserSecurityRoles.Standard
            : string.IsNullOrWhiteSpace(dto.SecurityRole)
                ? UserSecurityRoles.Standard
                : dto.SecurityRole.Trim();
        if (!UserSecurityRoles.IsValid(securityRole))
            return BadRequest("Invalid SecurityRole.");

        if (dto.UserType == TenantQueries.UserTypeFirm)
        {
            var adminId = User.GetUserId();
            if (adminId is null) return Unauthorized();
            var firmErr = await ValidateFirmIdsForAdminAsync(
                adminId.Value,
                effectiveHomeFirmId,
                selectedFirmIds,
                User.IsInRole(UserSecurityRoles.Admin),
                cancellationToken);
            if (firmErr != null)
                return BadRequest(firmErr);
        }

        entity.FullName = dto.FullName.Trim();
        entity.Email = emailNorm;
        entity.IsActive = dto.IsActive;
        entity.ProfileId = effectiveProfileId;
        entity.SecurityRole = securityRole;
        entity.UserType = dto.UserType;
        entity.FirmId = dto.UserType == TenantQueries.UserTypeFirm ? effectiveHomeFirmId : entity.FirmId;
        entity.CustomerId = dto.UserType == 1 ? null : dto.CustomerId;
        entity.JobTitleId = dto.JobTitleId;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value).AnyAsync(u => u.UserId == id))
                return NotFound();
            throw;
        }

        if (entity.UserType == TenantQueries.UserTypeFirm)
        {
            await _firmAccess.SyncMembershipsForFirmUserAsync(entity.UserId, effectiveHomeFirmId, selectedFirmIds, _context, cancellationToken);
        }

        if (entity.UserType == TenantQueries.UserTypeFirm)
        {
            var staleCustomerMemberships = await _context.UserCustomerMemberships
                .Where(m => m.UserId == entity.UserId)
                .ToListAsync(cancellationToken);
            if (staleCustomerMemberships.Count > 0)
            {
                _context.UserCustomerMemberships.RemoveRange(staleCustomerMemberships);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        if (entity.UserType == TenantQueries.UserTypeClient && entity.CustomerId is int homeCustomerId)
        {
            var adminId = User.GetUserId();
            if (adminId is null) return Unauthorized();
            var custErr = await ValidateCustomerIdsForAdminAsync(firmId.Value, dto.CustomerIds ?? [], cancellationToken);
            if (custErr != null)
                return BadRequest(custErr);
            await _customerAccess.SyncMembershipsForClientUserAsync(entity.UserId, homeCustomerId, dto.CustomerIds ?? [], _context, cancellationToken);
        }

        if (entity.UserType == TenantQueries.UserTypeClient)
        {
            var staleFirmMemberships = await _context.UserFirmMemberships
                .Where(m => m.UserId == entity.UserId)
                .ToListAsync(cancellationToken);
            if (staleFirmMemberships.Count > 0)
            {
                _context.UserFirmMemberships.RemoveRange(staleFirmMemberships);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return NoContent();
    }

    [HttpPut("{id:int}/password")]
    public async Task<IActionResult> PutUserPassword(int id, [FromBody] UserPasswordUpdateDto dto)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            return BadRequest("Password must be at least 6 characters.");

        entity.PasswordHash = EncryptPassword.HashPassword(dto.Password, PasswordSecretSuffix());
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:int}/profile-photo")]
    public async Task<IActionResult> GetProfilePhoto(int id)
    {
        var firmId = User.GetFirmId();
        var callerId = User.GetUserId();
        if (firmId is null || callerId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, callerId.Value) && callerId.Value != id)
            return Forbid();

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null || string.IsNullOrWhiteSpace(entity.ProfilePhotoPath)) return NotFound();

        return StoredFileResult.TryOpen(_fileStorage, entity.ProfilePhotoPath, Path.GetFileName(entity.ProfilePhotoPath), forceDownload: false) ?? NotFound();
    }

    [HttpPost("{id:int}/profile-photo")]
    [RequestSizeLimit(5_242_880)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<object>> UploadProfilePhoto(int id, IFormFile file)
    {
        var firmId = User.GetFirmId();
        var callerId = User.GetUserId();
        if (firmId is null || callerId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, callerId.Value) && callerId.Value != id)
            return Forbid();

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest("Archivo requerido.");
        if (file.Length > 5_242_880) return BadRequest("La imagen no puede superar 5 MB.");

        var ext = ExtensionForProfileImage(file.ContentType ?? "");
        if (ext == null) return BadRequest("Use JPG, PNG, WebP o GIF.");

        if (!string.IsNullOrWhiteSpace(entity.ProfilePhotoPath))
            _fileStorage.TryDeleteStoredFile(entity.ProfilePhotoPath);

        var storedFileName = $"{id}_{Guid.NewGuid():N}{ext}";
        var storageKey = _fileStorage.BuildKey("profiles", storedFileName);
        await _fileStorage.SaveUploadedFileAsync(file, storageKey);

        entity.ProfilePhotoPath = storageKey;
        await _context.SaveChangesAsync();

        return Ok(new { profilePhotoPath = entity.ProfilePhotoPath });
    }

    [HttpDelete("{id:int}/profile-photo")]
    public async Task<IActionResult> DeleteProfilePhoto(int id)
    {
        var firmId = User.GetFirmId();
        var callerId = User.GetUserId();
        if (firmId is null || callerId is null) return Unauthorized();
        if (await TenantQueries.UserIsClientPortalAsync(_context, callerId.Value) && callerId.Value != id)
            return Forbid();

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null) return NotFound();

        _fileStorage.TryDeleteStoredFile(entity.ProfilePhotoPath);
        entity.ProfilePhotoPath = null;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var firmId = User.GetFirmId();
        if (firmId is null) return Unauthorized();

        var currentUserId = User.GetUserId();
        if (currentUserId == id)
            return BadRequest("You cannot delete your own user account.");

        var entity = await TenantQueries.UsersVisibleInActiveFirm(_context, firmId.Value)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (entity == null) return NotFound();

        if (await TenantQueries.UserHasBlockingReferencesAsync(_context, id))
            return Conflict("This user cannot be deleted because they are linked to projects, teams, requests, or other records.");

        var tokens = await _context.UserRefreshTokens.Where(t => t.UserId == id).ToListAsync();
        _context.UserRefreshTokens.RemoveRange(tokens);
        var access = await _context.UserAccess.Where(a => a.UserId == id).ToListAsync();
        _context.UserAccess.RemoveRange(access);

        TryDeleteProfilePhoto(entity.ProfilePhotoPath);

        _context.Users.Remove(entity);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("This user cannot be deleted because they are still referenced.");
        }

        return NoContent();
    }

    private async Task<string?> ValidateFirmIdsForAdminAsync(
        int adminUserId,
        int newUserHomeFirmId,
        IList<int>? extraFirmIds,
        bool callerIsPlatformAdmin,
        CancellationToken cancellationToken)
    {
        if (extraFirmIds == null || extraFirmIds.Count == 0)
            return null;

        HashSet<int> allowed;
        if (callerIsPlatformAdmin)
        {
            allowed = await _context.Firms.AsNoTracking().Select(f => f.FirmId).ToHashSetAsync(cancellationToken);
        }
        else
        {
            allowed = (await _firmAccess.GetAccessibleFirmsAsync(adminUserId, _context, cancellationToken))
                .Select(f => f.Id)
                .ToHashSet();
        }

        foreach (var fid in extraFirmIds.Distinct())
        {
            if (fid == newUserHomeFirmId)
                continue;
            if (!allowed.Contains(fid))
                return "Una o más firmas no están permitidas para su usuario.";
            if (!await _context.Firms.AnyAsync(f => f.FirmId == fid, cancellationToken))
                return "Firma inválida.";
        }

        return null;
    }

    private async Task<string?> ValidateCustomerIdsForAdminAsync(
        int targetFirmId,
        IList<int>? extraCustomerIds,
        CancellationToken cancellationToken)
    {
        if (extraCustomerIds == null || extraCustomerIds.Count == 0)
            return null;

        var allowedCustomerIds = await _context.Customers.AsNoTracking()
            .Where(c => c.FirmId == targetFirmId)
            .Select(c => c.CustomerId)
            .ToHashSetAsync(cancellationToken);

        foreach (var cid in extraCustomerIds.Distinct())
        {
            if (!allowedCustomerIds.Contains(cid))
                return "Una o más empresas cliente no pertenecen a su firma o no están permitidas.";
        }

        return null;
    }

    private async Task<string> ValidateUserWriteAsync(int userType, int? customerId, int firmId)
    {
        if (userType == 1)
        {
            if (customerId != null)
                return "Firm users (UserType 1) must not have a CustomerId.";
            return "";
        }

        if (userType == 2)
        {
            if (customerId is null)
                return "Client users (UserType 2) require a CustomerId.";
            if (!await TenantQueries.CustomerBelongsToFirmAsync(_context, customerId.Value, firmId))
                return "Customer does not belong to your firm.";
            return "";
        }

        return "UserType must be 1 (Firm) or 2 (Client).";
    }

    private void TryDeleteProfilePhoto(string? storedPath)
    {
        _fileStorage.TryDeleteStoredFile(storedPath);
    }

    private static string? ExtensionForProfileImage(string contentType)
    {
        var ct = contentType.ToLowerInvariant().Trim();
        return ct switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => null
        };
    }

    private async Task<UserListDto> GetUserDtoAsync(int userId, int activeFirmId)
    {
        var dto = await TenantQueries.UsersVisibleInActiveFirm(_context, activeFirmId)
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new UserListDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                ProfileId = u.ProfileId,
                ProfileName = u.Profile.Name,
                SecurityRole = u.SecurityRole,
                UserType = u.UserType,
                FirmId = u.FirmId,
                CustomerId = u.CustomerId,
                CustomerName = u.Customer != null ? u.Customer.Name : "",
                JobTitleId = u.JobTitleId,
                JobTitleName = u.JobTitle.Name,
                UserGuid = u.UserGuid ?? "",
                ProfilePhotoPath = u.ProfilePhotoPath,
                ManagedFirmIds = u.UserType == TenantQueries.UserTypeFirm
                    ? u.UserFirmMemberships.Select(m => m.FirmId).ToList()
                    : new List<int> { u.FirmId },
                ManagedCustomerIds = u.UserType == TenantQueries.UserTypeClient
                    ? u.UserCustomerMemberships.Select(m => m.CustomerId).ToList()
                    : new List<int>()
            })
            .FirstAsync();
        NormalizeClientUserDto(dto);
        return dto;
    }

    private static void NormalizeClientUserDto(UserListDto dto)
    {
        if (dto.UserType != TenantQueries.UserTypeClient || dto.CustomerId is not int home)
            return;
        if (!dto.ManagedCustomerIds.Contains(home))
            dto.ManagedCustomerIds.Add(home);
    }
}
