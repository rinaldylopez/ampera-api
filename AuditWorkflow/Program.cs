using AuditWorkflow.Data;
using AuditWorkflow.Helpers;
using AuditWorkflow.Services;
using AuditWorkflow.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using AuditWorkflow.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52_428_800; // 50 MB
});

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(Environment.GetEnvironmentVariable("DefaultConnection") ?? builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Audit API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = @"JWT Authorization header using the Bearer scheme. \r\n\r\n 
                      Enter 'Bearer' [space] and then your token in the text input below.
                      \r\n\r\nExample: 'Bearer 12345abcdef'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
            Scheme = "oauth2",
            Name = "Bearer",
            In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

/// <summary>
/// Configuraci?n para JWT
/// </summary>
/// <returns></returns>
//Configuraciones para JWT
var appSettingsSection = builder.Configuration.GetSection("AppSettings");
builder.Services.Configure<AppSettings>(appSettingsSection);

//JWT
var appSetting = appSettingsSection.Get<AppSettings>();
var jwtSecret = Environment.GetEnvironmentVariable("Secret") ?? appSetting?.Secret;
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException("JWT signing secret is not configured. Set environment variable Secret or AppSettings:Secret.");
var KeySecret = Encoding.ASCII.GetBytes(jwtSecret);

var authCookieSection = builder.Configuration.GetSection(AuthCookieSettings.SectionName);
builder.Services.Configure<AuthCookieSettings>(authCookieSection);
var accessCookieName = authCookieSection.Get<AuthCookieSettings>()?.AccessCookieName ?? "ampera_access";

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(KeySecret),
        ValidateIssuer = false,
        ValidateAudience = false,
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = ClaimTypes.Role
    };
    x.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(accessCookieName, out var cookieJwt)
                && !string.IsNullOrWhiteSpace(cookieJwt))
            {
                context.Token = cookieJwt;
                return Task.CompletedTask;
            }

            var authHeader = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authHeader) &&
                authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = authHeader["Bearer ".Length..].Trim();
                return Task.CompletedTask;
            }

            if (context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                var fromQuery = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(fromQuery))
                    context.Token = fromQuery;
            }

            return Task.CompletedTask;
        }
    };
});

/// <summary>
/// Ejecutar la independencia del servicio para JWT.
/// </summary>
/// <returns></returns>
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IProfileResourcePermissionService, ProfileResourcePermissionService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<AuthCookieService>();
builder.Services.AddScoped<IUserFirmAccessService, UserFirmAccessService>();
builder.Services.AddScoped<IUserCustomerAccessService, UserCustomerAccessService>();
builder.Services.AddScoped<ILoginEmailChallengeService, LoginEmailChallengeService>();
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
// IIS y algunos hosts no exponen bien la jerarquía: refuerzo desde variables de entorno conocidas.
builder.Services.PostConfigure<EmailSettings>(opts =>
{
    var envHost =
        Environment.GetEnvironmentVariable("Email__SmtpHost") ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_HOST");
    if (!string.IsNullOrWhiteSpace(envHost))
        opts.SmtpHost = envHost.Trim();

    var envPort =
        Environment.GetEnvironmentVariable("Email__SmtpPort") ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_PORT");
    if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort.Trim(), out var parsedPort) && parsedPort > 0 && parsedPort <= 65535)
        opts.SmtpPort = parsedPort;

    // Contraseña SMTP: la configuración normal ya fusiona appsettings + appsettings.{Env} +
    // User Secrets (Development) + variables de entorno. Si solo cambió appsettings pero sigue la clave vieja,
    // ejecute dotnet user-secrets list (AuditWorkflow tiene UserSecretsId en .csproj). Si define Email__Password
    // o EMAIL_SMTP_PASSWORD en el proceso del host, esa clave debe prevalecer.
    var envPwd =
        Environment.GetEnvironmentVariable("Email__Password")
        ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_PASSWORD");
    if (!string.IsNullOrWhiteSpace(envPwd))
        opts.Password = envPwd.Trim();
    else
        opts.Password = (opts.Password ?? "").Trim();

    if (string.IsNullOrWhiteSpace(opts.PortalBaseUrl))
    {
        var portal =
            Environment.GetEnvironmentVariable("Email__PortalBaseUrl")
            ?? Environment.GetEnvironmentVariable("EMAIL_PORTAL_BASE_URL");
        if (!string.IsNullOrWhiteSpace(portal))
            opts.PortalBaseUrl = portal.Trim().TrimEnd('/');
    }
});

builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.Configure<DueReminderJobSettings>(builder.Configuration.GetSection("DueReminderJob"));
builder.Services.Configure<FileStorageSettings>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IRequestDueReminderService, RequestDueReminderService>();
builder.Services.AddScoped<IRequestOverdueWeeklyReminderService, RequestOverdueWeeklyReminderService>();
builder.Services.AddHostedService<RequestDueReminderBackgroundService>();

builder.Services.AddSingleton<IRequestCommentsRealtimeNotifier, RequestCommentsRealtimeNotifier>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddCors(o =>
{
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    var envCors = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
    if (!string.IsNullOrWhiteSpace(envCors))
    {
        corsOrigins = envCors
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    if (corsOrigins.Length == 0)
    {
        corsOrigins =
        [
            "http://localhost:5173",
            "https://www.ampera-ampc.com"
        ];
    }

    // En Development el SPA Vite (http://localhost:5173) debe estar permitido aunque
    // appsettings.json solo liste dominios de producción.
    if (builder.Environment.IsDevelopment())
    {
        corsOrigins = corsOrigins
            .Concat(["http://localhost:5173", "https://localhost:5173"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    o.AddDefaultPolicy(p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// Comprueba en logs (sin imprimir la clave) que Email se fusionó: JSON + variables de entorno (p. ej. Email__Password).
{
    var email = app.Services.GetRequiredService<IOptions<EmailSettings>>().Value;
    app.Logger.LogInformation(
        "Email SMTP: Enabled={Enabled}, Host={Host}, User={User}, From={From}, PasswordConfigured={PasswordOk}, PortalBaseUrl={Portal}",
        email.Enabled,
        email.SmtpHost,
        email.User,
        email.FromAddress,
        !string.IsNullOrWhiteSpace(email.Password),
        email.PortalBaseUrl);

    var fileStorage = app.Services.GetRequiredService<IFileStorageService>();
    app.Logger.LogInformation("File storage root: {Root}", fileStorage.RootPath);
}

/// <summary>
/// Configure the HTTP request pipeline.
/// </summary>
/// <returns></returns>
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


/// <summary>
/// Configure the HTTP request pipeline.
/// </summary>
/// <returns></returns>
// En Development el front (http://5173) llama al API por http://5289; redirigir a HTTPS rompe CORS/cookies.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors();

// Los documentos de auditoría ya no se sirven como estáticos públicos bajo /uploads.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<RequestCommentsHub>("/hubs/request-comments");
app.MapControllers();

if (args.Any(static a => string.Equals(a, "--seed", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var appSettings = scope.ServiceProvider.GetRequiredService<IOptions<AppSettings>>().Value ?? new AppSettings();
    var passwordSecretSuffix = Environment.GetEnvironmentVariable("Secret") ?? appSettings.Secret ?? "";
    await DbSeeder.SeedAsync(db, passwordSecretSuffix);
    return;
}

app.Run();
