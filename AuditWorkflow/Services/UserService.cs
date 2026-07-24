using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Auth;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Services
{
    public class UserService(
        ITokenService tokenService,
        IUserFirmAccessService firmAccess,
        IUserCustomerAccessService customerAccess,
        IProfileResourcePermissionService profileResourcePermissions,
        IOptions<AppSettings> appSettings) : IUserService
    {
        private readonly ITokenService _tokenService = tokenService;
        private readonly IUserFirmAccessService _firmAccess = firmAccess;
        private readonly IUserCustomerAccessService _customerAccess = customerAccess;
        private readonly IProfileResourcePermissionService _profileResourcePermissions = profileResourcePermissions;
        private readonly AppSettings _appSettings = appSettings.Value ?? new AppSettings();

        public async Task<User?> FindAuthenticatedUserAsync(AuthRequest model, AppDbContext context)
        {
            var plain = model.ResolvePlainPassword();
            if (string.IsNullOrWhiteSpace(plain) || string.IsNullOrWhiteSpace(model.Email))
                return null;

            var secretSuffix = Environment.GetEnvironmentVariable("Secret") ?? _appSettings.Secret ?? "";
            string pass = EncryptPassword.HashPassword(plain, secretSuffix);

            return await context.Users
                .Include(t => t.Firm)
                .Where(x => x.Email == model.Email && x.PasswordHash == pass && x.IsActive == true)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .FirstOrDefaultAsync();
        }

        public async Task<UserResponse> CreateLoginResponseAsync(User usuario, AppDbContext context, int activeFirmId, int? preferredActiveCustomerId = null, CancellationToken cancellationToken = default)
        {
            var userResponse = await BuildUserProfileResponseAsync(
                usuario, context, activeFirmId, preferredActiveCustomerId, cancellationToken);

            dynamic token = await _tokenService.GetTokenAsync(usuario, context, activeFirmId, userResponse.CustomerId);

            userResponse.UserToken = token.accessToken;
            userResponse.RefreshToken = token.refreshToken;
            userResponse.ExpirationTime = token.expiration;
            userResponse.Expires = token.expires;

            return userResponse;
        }

        public async Task<UserResponse> BuildSessionResponseAsync(
            User usuario,
            AppDbContext context,
            int activeFirmId,
            int? activeCustomerId,
            DateTime accessExpiresUtc,
            int accessMinutes,
            CancellationToken cancellationToken = default)
        {
            var userResponse = await BuildUserProfileResponseAsync(
                usuario, context, activeFirmId, activeCustomerId, cancellationToken);
            userResponse.UserToken = "";
            userResponse.RefreshToken = "";
            userResponse.ExpirationTime = accessExpiresUtc;
            userResponse.Expires = accessMinutes;
            return userResponse;
        }

        private async Task<UserResponse> BuildUserProfileResponseAsync(
            User usuario,
            AppDbContext context,
            int activeFirmId,
            int? preferredActiveCustomerId,
            CancellationToken cancellationToken)
        {
            var activeName = await context.Firms.AsNoTracking()
                .Where(f => f.FirmId == activeFirmId)
                .Select(f => f.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "";

            var accessibleFirms = await _firmAccess.GetAccessibleFirmsAsync(usuario.UserId, context);
            var accessibleCustomers = usuario.UserType == TenantQueries.UserTypeClient
                ? (await _customerAccess.GetAccessibleCustomersAsync(usuario.UserId, context)).ToList()
                : [];

            int? sessionCustomerId = null;
            if (usuario.UserType == TenantQueries.UserTypeClient)
            {
                sessionCustomerId = await ResolveActiveCustomerIdAsync(usuario, context, preferredActiveCustomerId);
                if (sessionCustomerId is null && accessibleCustomers.Count > 0)
                    sessionCustomerId = accessibleCustomers[0].Id;
            }

            UserResponse userResponse = new()
            {
                Email = usuario.Email,
                FullName = usuario.FullName,
                UserId = usuario.UserId,
                ProfileId = usuario.ProfileId,
                FirmId = activeFirmId,
                FirmName = activeName,
                UserType = usuario.UserType,
                CustomerId = sessionCustomerId,
                SecurityRole = usuario.SecurityRole ?? "Standard",
                ProfilePhotoPath = usuario.ProfilePhotoPath,
                MustChangePassword = usuario.MustChangePassword,
                AccessibleFirms = accessibleFirms.ToList(),
                AccessibleCustomers = accessibleCustomers
            };

            userResponse.Menu = [.. await GetMenuByRolId(usuario.ProfileId, context)];

            var isSecurityAdmin =
                string.Equals(usuario.SecurityRole, UserSecurityRoles.Admin, StringComparison.OrdinalIgnoreCase);
            userResponse.ResourcePermissions =
                usuario.UserType == TenantQueries.UserTypeClient
                    ? ProfileResourcePermissionsDto.FullAccess
                    : await _profileResourcePermissions.ResolveDtoForLoginAsync(
                        usuario.ProfileId,
                        isSecurityAdmin,
                        cancellationToken);

            return userResponse;
        }

        private async Task<int?> ResolveActiveCustomerIdAsync(User usuario, AppDbContext context, int? preferredActiveCustomerId)
        {
            if (usuario.UserType != TenantQueries.UserTypeClient)
                return null;

            if (preferredActiveCustomerId is int p &&
                await _customerAccess.CanAccessCustomerAsync(usuario.UserId, p, context))
                return p;

            var list = await _customerAccess.GetAccessibleCustomersAsync(usuario.UserId, context);
            if (list.Count == 0)
                return usuario.CustomerId;

            if (usuario.CustomerId is int home && list.Any(x => x.Id == home))
                return home;

            return list[0].Id;
        }

        public async Task<UserResponse> Auth(AuthRequest model, AppDbContext context)
        {
            var usuario = await FindAuthenticatedUserAsync(model, context);
            if (usuario == null) return null;
            return await CreateLoginResponseAsync(usuario, context, usuario.FirmId);
        }

        private static async Task<ICollection<Menu>> GetMenuByRolId(int profileId, AppDbContext context)
        {
            var resultItems = await context.ProfileItems
                .Include(t => t.NavigationItem).ThenInclude(m => m.Module)
                .Where(x => x.ProfileId == profileId)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .ToListAsync();

            var modules = resultItems
                .Select(x => x.NavigationItem.Module)
                .GroupBy(m => m.ModuleId)
                .Select(g => g.First())
                .OrderBy(m => m.ModuleId)
                .ToList();

            List<Menu> menuList = [];

            foreach (var module in modules)
            {
                var itemsForModule = resultItems
                    .Where(x => x.NavigationItem.ModuleId == module.ModuleId)
                    .Select(x => x.NavigationItem)
                    .GroupBy(n => n.NavigationItemId)
                    .Select(g => g.First())
                    .OrderBy(n => n.NavigationItemId)
                    .ToList();

                menuList.Add(new Menu
                {
                    ModuleId = module.ModuleId,
                    Title = module.Name,
                    Icon = module.Icon ?? "",
                    To = "#",
                    Children = itemsForModule.ConvertAll(n => new Children
                    {
                        NavigationItemId = n.NavigationItemId,
                        Title = n.Name,
                        Icon = "CircleDotIcon",
                        To = n.Path
                    })
                });
            }

            return menuList;
        }
    }
}
