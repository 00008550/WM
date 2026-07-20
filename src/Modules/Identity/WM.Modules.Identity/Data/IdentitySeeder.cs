using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Data;

/// <summary>Development seed: Admin role with every permission and one admin account.</summary>
public sealed class IdentitySeeder(
    IdentityDbContext db,
    IPasswordHasher<User> hasher,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
            return;

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
        admin.PasswordHash = hasher.HashPassword(admin, "Admin!234");
        admin.Roles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id, Role = adminRole });

        db.Roles.AddRange(adminRole, managerRole, employeeRole);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded identity: admin / Admin!234 (development only — change immediately)");
    }
}
