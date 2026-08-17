using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Services;
using WM.Modules.People.Contracts;

namespace WM.Api.Infrastructure;

/// <summary>
/// Cross-module demo seed (dev only): links a few users to seeded employees so the
/// app is immediately usable "as TLW" — sign in as an employee for self-service, or
/// as a manager. Runs after Identity + People seeders, in the API host because it
/// spans both modules.
/// </summary>
public sealed class DemoUserSeeder(
    IdentityDbContext db,
    IEmployeeDirectory employees,
    UserManagementService users,
    ILogger<DemoUserSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.EmployeeId != null, ct))
            return; // already linked demo users

        var employeeRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Employee", ct);
        var managerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Manager", ct);
        if (employeeRole is null || managerRole is null)
            return;

        // Seeding runs at startup with no signed-in user, so it must bypass data scope.
        var active = await employees.ListEmployedOnUnscopedAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);
        if (active.Count < 3)
            return;

        // First employee → a Manager self-service+team user; next two → plain Employees.
        var manager = active[0];
        await Create(manager.Code, manager.FullName, "manager", "Manager!234", managerRole.Id, manager.Id, ct);

        var seeded = new List<string> { "manager" };
        foreach (var employee in active.Skip(1).Take(2))
        {
            var userName = FirstName(employee.FullName).ToLowerInvariant();
            userName = await Unique(userName, ct);
            if (await Create(employee.Code, employee.FullName, userName, "Employee!234", employeeRole.Id, employee.Id, ct))
                seeded.Add($"{userName} (employee {employee.Code})");
        }

        logger.LogInformation("Seeded employee-linked demo users: {Users}. Passwords: manager=Manager!234, employees=Employee!234",
            string.Join(", ", seeded));
    }

    private async Task<bool> Create(
        string code, string fullName, string userName, string password, Guid roleId, Guid employeeId, CancellationToken ct)
    {
        var result = await users.CreateAsync(new CreateUserRequest(
            userName, $"{userName}@wm.demo", fullName, password, employeeId, [roleId]), ct);
        if (!result.Succeeded)
            logger.LogWarning("Demo user {User} for employee {Code} not created: {Error}", userName, code, result.Error);
        return result.Succeeded;
    }

    private async Task<string> Unique(string baseName, CancellationToken ct)
    {
        var name = baseName;
        var i = 1;
        while (await db.Users.AnyAsync(u => u.UserName == name, ct))
            name = $"{baseName}{++i}";
        return name;
    }

    private static string FirstName(string fullName) =>
        new(fullName.Split(' ')[0].Where(char.IsLetter).ToArray());
}
