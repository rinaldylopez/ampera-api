using AuditWorkflow.Data;
using AuditWorkflow.Extensions;
using AuditWorkflow.Helpers;
using AuditWorkflow.Models.Auth;
using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuditWorkflow.Controllers.Auth
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserAuthController(
        ILoginEmailChallengeService loginChallengeService,
        IPasswordResetService passwordResetService,
        AppDbContext context,
        ITokenService tokenService,
        IUserService userService,
        IUserFirmAccessService firmAccess,
        IUserCustomerAccessService customerAccess,
        AuthCookieService authCookies,
        IOptions<AppSettings> appSettings) : ControllerBase
    {
        private readonly AppDbContext _context = context;
        private readonly ILoginEmailChallengeService _loginChallengeService = loginChallengeService;
        private readonly IPasswordResetService _passwordResetService = passwordResetService;
        private readonly ITokenService _tokenService = tokenService;
        private readonly IUserService _userService = userService;
        private readonly IUserFirmAccessService _firmAccess = firmAccess;
        private readonly IUserCustomerAccessService _customerAccess = customerAccess;
        private readonly AuthCookieService _authCookies = authCookies;
        private readonly AppSettings _appSettings = appSettings.Value ?? new AppSettings();

        private string PasswordSecretSuffix() =>
            Environment.GetEnvironmentVariable("Secret") ?? _appSettings.Secret ?? "";

        private ActionResult<UserResponse> OkAuthResponse(UserResponse response)
        {
            if (!string.IsNullOrWhiteSpace(response.UserToken) && !string.IsNullOrWhiteSpace(response.RefreshToken))
                _authCookies.SetAuthCookies(Response, response.UserToken, response.RefreshToken, response.Expires);
            _authCookies.StripTokensFromResponse(response);
            return Ok(response);
        }

        private IActionResult OkTokenRefresh(dynamic tokenResult)
        {
            string accessToken = tokenResult.accessToken;
            string refreshToken = tokenResult.refreshToken;
            int expires = tokenResult.expires;
            _authCookies.SetAuthCookies(Response, accessToken, refreshToken, expires);
            return Ok(new
            {
                expires,
                expiration = tokenResult.expiration
            });
        }

        /// <summary>Perfil de sesión actual (cookies HttpOnly); no rota refresh.</summary>
        [Authorize]
        [HttpGet("session")]
        public async Task<ActionResult<UserResponse>> GetSession(CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var activeFirmId = User.GetFirmId();
            var activeCustomerId = User.GetCustomerId();
            if (userId is null || activeFirmId is null)
                return Unauthorized();

            var usuario = await _context.Users
                .Include(u => u.Firm)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
            if (usuario == null)
                return Unauthorized();

            var accessExpires = DateTime.UtcNow;
            var expClaim = User.FindFirst("exp")?.Value;
            if (long.TryParse(expClaim, out var unixExp))
                accessExpires = DateTimeOffset.FromUnixTimeSeconds(unixExp).UtcDateTime;

            var accessMinutes = _appSettings.JwtAccessTokenMinutes;
            if (accessMinutes <= 0)
                accessMinutes = 60;

            var response = await _userService.BuildSessionResponseAsync(
                usuario,
                _context,
                activeFirmId.Value,
                activeCustomerId,
                accessExpires,
                accessMinutes,
                cancellationToken);
            return Ok(response);
        }

        /// <summary>Paso 1: credenciales correctas → envía código de 6 dígitos al correo.</summary>
        [HttpPost("login-request-code")]
        public async Task<ActionResult<LoginRequestCodeResponse>> LoginRequestCode(
            [FromBody] AuthRequest authRequest,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await _loginChallengeService.RequestCodeAsync(authRequest, _context, cancellationToken);
                if (response == null)
                    return NoContent();
                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message == LoginEmailChallengeService.EmailNotConfiguredMessage)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Servicio de correo no configurado." });
            }
        }

        /// <summary>Paso 2: código recibido por correo → emite tokens.</summary>
        [HttpPost("login-verify-code")]
        public async Task<ActionResult<UserResponse>> LoginVerifyCode(
            [FromBody] LoginVerifyCodeRequest request,
            CancellationToken cancellationToken)
        {
            var userResponse = await _loginChallengeService.VerifyCodeAsync(request, _context, cancellationToken);
            if (userResponse == null)
                return Unauthorized(new { message = "Código incorrecto o sesión expirada." });

            var userAccess = new UserAccess
            {
                UserId = userResponse.UserId,
                DateAcces = DateTime.Now,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
                MachineName = Environment.MachineName
            };

            try
            {
                _context.UserAccess.Add(userAccess);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                return NoContent();
            }

            return OkAuthResponse(userResponse);
        }
        /// <summary>Valida si un enlace de restablecimiento sigue vigente (sin revelar el usuario).</summary>
        [AllowAnonymous]
        [HttpGet("password-reset/validate")]
        public async Task<IActionResult> ValidatePasswordResetToken(
            [FromQuery] string token,
            CancellationToken cancellationToken)
        {
            var ok = await _passwordResetService.IsTokenValidAsync(token ?? "", _context, cancellationToken);
            if (!ok)
                return BadRequest(new { message = "El enlace no es válido o ha expirado." });
            return Ok(new { valid = true, expiresInMinutes = PasswordResetService.TokenExpiryMinutes });
        }

        /// <summary>Define una nueva contraseña con el token del correo (un solo uso).</summary>
        [AllowAnonymous]
        [HttpPost("password-reset/complete")]
        public async Task<IActionResult> CompletePasswordReset(
            [FromBody] ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Trim().Length < 6)
                return BadRequest(new { message = "La nueva contraseña debe tener al menos 6 caracteres." });

            var ok = await _passwordResetService.CompleteResetAsync(
                request.Token ?? "",
                request.NewPassword,
                _context,
                cancellationToken);
            if (!ok)
                return BadRequest(new { message = "El enlace no es válido o ha expirado." });

            return Ok(new { message = "Contraseña actualizada. Ya puede iniciar sesión." });
        }

        /// <summary>Cambia la contraseña del usuario autenticado (p. ej. tras contraseña provisional).</summary>
        [Authorize]
        [HttpPost("change-password")]
        public async Task<ActionResult<UserResponse>> ChangePassword(
            [FromBody] ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var activeFirmId = User.GetFirmId();
            var activeCustomerId = User.GetCustomerId();
            if (userId is null || activeFirmId is null)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                return BadRequest(new { message = "La nueva contraseña debe tener al menos 6 caracteres." });

            var entity = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
            if (entity == null)
                return Unauthorized();

            var suffix = PasswordSecretSuffix();
            var currentHash = EncryptPassword.HashPassword(request.CurrentPassword ?? "", suffix);
            if (entity.PasswordHash != currentHash)
                return BadRequest(new { message = "La contraseña actual no es correcta." });

            entity.PasswordHash = EncryptPassword.HashPassword(request.NewPassword, suffix);
            entity.MustChangePassword = false;
            await _context.SaveChangesAsync(cancellationToken);

            var usuario = await _context.Users
                .Include(u => u.Firm)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .FirstAsync(u => u.UserId == entity.UserId, cancellationToken);

            var response = await _userService.CreateLoginResponseAsync(usuario, _context, activeFirmId.Value, activeCustomerId);
            return OkAuthResponse(response);
        }

        /// <summary>Cambia la firma activa y obtiene nuevos tokens (misma sesión de refresh).</summary>
        [Authorize]
        [HttpPost("switch-firm")]
        public async Task<ActionResult<UserResponse>> SwitchFirm(
            [FromBody] SwitchFirmRequest request,
            CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            if (userId is null)
                return Unauthorized();
            if (!await _firmAccess.CanAccessFirmAsync(userId.Value, request.FirmId, _context, cancellationToken))
                return BadRequest(new { message = "No tiene acceso a esa firma." });

            var usuario = await _context.Users
                .Include(u => u.Firm)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
            if (usuario == null)
                return Unauthorized();

            var response = await _userService.CreateLoginResponseAsync(usuario, _context, request.FirmId);
            return OkAuthResponse(response);
        }

        /// <summary>Cambia el cliente (empresa) activo en portal y obtiene nuevos tokens.</summary>
        [Authorize]
        [HttpPost("switch-customer")]
        public async Task<ActionResult<UserResponse>> SwitchCustomer(
            [FromBody] SwitchCustomerRequest request,
            CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            if (userId is null)
                return Unauthorized();
            if (!await _customerAccess.CanAccessCustomerAsync(userId.Value, request.CustomerId, _context, cancellationToken))
                return BadRequest(new { message = "No tiene acceso a esa empresa." });

            var usuario = await _context.Users
                .Include(u => u.Firm)
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
            if (usuario == null)
                return Unauthorized();

            var response = await _userService.CreateLoginResponseAsync(usuario, _context, usuario.FirmId, request.CustomerId);
            return OkAuthResponse(response);
        }

        [HttpPost("user-token-refresh")]
        public async Task<IActionResult> TokenRefresh([FromBody] TokenRequest? request)
        {
            var refreshToken = _authCookies.ReadRefreshToken(Request, request?.RefreshToken);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized("Sesión no encontrada.");

            var storedToken = await _context.UserRefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (storedToken == null)
                return Unauthorized("Sesión no encontrada.");
            if (storedToken.IsRevoked)
                return Unauthorized("Sesión revocada.");
            if (storedToken.ExpiresAt < DateTime.Now)
                return Unauthorized("La sesión ha expirado.");

            var user = await _context.Users.FindAsync(storedToken.UserId);
            if (user == null)
                return Unauthorized("Usuario no encontrado.");

            if (!await _firmAccess.CanAccessFirmAsync(storedToken.UserId, storedToken.ActiveFirmId, _context))
                return Unauthorized("La sesión ya no es válida para esta firma.");

            if (user.UserType == TenantQueries.UserTypeClient)
            {
                if (storedToken.ActiveCustomerId is not int ac
                    || !await _customerAccess.CanAccessCustomerAsync(storedToken.UserId, ac, _context))
                    return Unauthorized("La sesión ya no es válida para esta empresa.");
                var result = await _tokenService.GetTokenAsync(user, _context, storedToken.ActiveFirmId, ac);
                return OkTokenRefresh(result);
            }

            var resultFirm = await _tokenService.GetTokenAsync(user, _context, storedToken.ActiveFirmId, null);
            return OkTokenRefresh(resultFirm);
        }

        [HttpPost("user-token-revoke")]
        public async Task<IActionResult> TokenRevoke([FromBody] TokenRequest? request)
        {
            var refreshToken = _authCookies.ReadRefreshToken(Request, request?.RefreshToken);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _authCookies.ClearAuthCookies(Response);
                return Ok();
            }

            var storedToken = await _context.UserRefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (storedToken == null)
            {
                _authCookies.ClearAuthCookies(Response);
                return Ok();
            }

            storedToken.IsRevoked = true;
            storedToken.IsUsed = true;
            await _context.SaveChangesAsync();

            _authCookies.ClearAuthCookies(Response);
            return Ok();
        }
    }

    public class TokenRequest
    {
        public string? RefreshToken { get; set; }
    }

    public class SwitchFirmRequest
    {
        public int FirmId { get; set; }
    }

    public class SwitchCustomerRequest
    {
        public int CustomerId { get; set; }
    }
}
