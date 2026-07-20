using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Data;

/// <summary>
/// Bootstrap seed: the built-in roles and a single administrator account, so a
/// fresh install has exactly one way in. The initial password must be supplied
/// via <c>Bootstrap:AdminPassword</c> — never hard-coded for a real deployment.
/// </summary>
public sealed class IdentitySeeder(
    IdentityDbContext db,
    IPasswordHasher<User> hasher,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        var adminPassword = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogError(
                "No Bootstrap:AdminPassword configured — the administrator account was NOT created. " +
                "Set it (env: Bootstrap__AdminPassword) and restart to complete installation.");
            return;
        }
        if (adminPassword.Length < 12)
        {
            logger.LogError("Bootstrap:AdminPassword must be at least 12 characters — administrator account NOT created.");
            return;
        }

        var adminRole = new Role
        {
            Name = "Administrator",
            Description = "Full access to every module.",
            IsSystem = true,
            Permissions = WmPermissions.All.Select(p => new RolePermission { Permission = p }).ToList(),
        };

        var managerRole = new Role
        {
            Name = "Manager",
            Description = "Team management: people, attendance, timesheets (scoped to their team).",
            IsSystem = true,
            Permissions = new[]
                {
                    WmPermissions.EmployeesView, WmPermissions.AttendanceView,
                    WmPermissions.PunchesRecord, WmPermissions.TimesheetsEdit,
                    WmPermissions.SelfService,
                }
                .Select(p => new RolePermission { Permission = p }).ToList(),
        };

        var employeeRole = new Role
        {
            Name = "Employee",
            Description = "Self-service on their own record only.",
            IsSystem = true,
            Permissions = new[] { WmPermissions.SelfService }
                .Select(p => new RolePermission { Permission = p }).ToList(),
        };

        var admin = new User
        {
            UserName = "admin",
            Email = "admin@wm.local",
            DisplayName = "WM Administrator",
        };
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        admin.Roles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id, Role = adminRole });

        db.Roles.AddRange(adminRole, managerRole, employeeRole);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Seeded built-in roles and the 'admin' account using the configured bootstrap password. Change it after first sign-in.");
    }
}
