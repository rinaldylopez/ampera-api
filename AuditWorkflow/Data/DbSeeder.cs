using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using AuditWorkflow.Helpers;
using AuditWorkflow.Settings;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Data;

/// <summary>
/// Datos mínimos para entrar a la plataforma (login + menú JWT vía perfiles en BD).
/// Uso (desde la carpeta del proyecto): <c>dotnet run -- --seed</c>
/// Credenciales por defecto: <c>admin@ampera.local</c> / <c>Ampera123!</c>
/// El sufijo del hash debe ser el mismo que en login: <c>Environment Secret ?? AppSettings:Secret</c>.
/// </summary>
public static class DbSeeder
{
    public const string SeedAdminEmail = "admin@ampera.local";
    public const string SeedAdminPassword = "Ampera123!";

    public static async Task SeedAsync(AppDbContext db, string passwordSecretSuffix)
    {
        await db.Database.MigrateAsync();

        if (await db.Users.AnyAsync(u => u.Email == SeedAdminEmail))
        {
            Console.WriteLine($"DbSeeder: ya existe {SeedAdminEmail}; no se insertaron datos.");
            return;
        }

        var country = new Country { Name = "El Salvador", IsoCode = "SV", PhonePrefix = "+503" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var firm = new Firm
        {
            Name = "Firma Demo AMPERA",
            TaxId = "0614-000000-000-0",
            Phone = "0000-0000",
            Email = "contacto@firma.demo"
        };
        db.Firms.Add(firm);
        await db.SaveChangesAsync();

        var jobTitle = new JobTitle
        {
            Name = "Socio / Administrador",
            Description = "Acceso de demostración"
        };
        db.JobTitle.Add(jobTitle);
        await db.SaveChangesAsync();

        var profile = new Profile { Name = "Administrador" };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var module = new Module { Name = "AMPERA", Icon = "LayoutDashboardIcon" };
        db.Modules.Add(module);
        await db.SaveChangesAsync();

        var routes = new (string Name, string Path)[]
        {
            ("Inicio", "/dashboard"),
            ("Mi firma", "/firm"),
            ("Clientes", "/customers"),
            ("Actividades económicas", "/economic-activities"),
            ("Equipos", "/teams"),
            ("Proyectos", "/projects"),
            ("Solicitudes (PBC)", "/requests"),
            ("Usuarios", "/users"),
            ("Puestos", "/job-titles")
        };

        var navItems = routes
            .Select(r => new NavigationItem
            {
                ModuleId = module.ModuleId,
                Name = r.Name,
                Path = r.Path
            })
            .ToList();

        db.NavigationItems.AddRange(navItems);
        await db.SaveChangesAsync();

        foreach (var ni in navItems)
        {
            db.ProfileItems.Add(new ProfileItem
            {
                ProfileId = profile.ProfileId,
                NavigationItemId = ni.NavigationItemId
            });
        }

        await db.SaveChangesAsync();

        await ClientPortalProfileHelper.EnsureProfileExistsAsync(db);

        var passwordHash = EncryptPassword.HashPassword(SeedAdminPassword, passwordSecretSuffix);

        var adminUser = new User
        {
            FullName = "Administrador AMPERA",
            Email = SeedAdminEmail,
            PasswordHash = passwordHash,
            IsActive = true,
            ProfileId = profile.ProfileId,
            SecurityRole = "Admin",
            UserType = 1,
            FirmId = firm.FirmId,
            CustomerId = null,
            JobTitleId = jobTitle.JobTitleId,
            UserGuid = Guid.NewGuid().ToString("D"),
            MustChangePassword = false
        };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();

        db.UserFirmMemberships.Add(new UserFirmMembership { UserId = adminUser.UserId, FirmId = firm.FirmId });
        await db.SaveChangesAsync();

        Console.WriteLine("DbSeeder: datos insertados correctamente.");
        Console.WriteLine($"  Email: {SeedAdminEmail}");
        Console.WriteLine($"  Contraseña: {SeedAdminPassword}");
    }
}
