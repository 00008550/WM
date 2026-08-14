using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.People.Domain;

namespace WM.Modules.People.Data;

/// <summary>Deterministic demo data so every dev environment looks alive on first run.</summary>
public sealed class PeopleSeeder(PeopleDbContext db, ILogger<PeopleSeeder> logger)
{
    private static readonly string[] FirstNames =
        ["Ava", "Liam", "Maja", "Noah", "Zoe", "Luca", "Nina", "Theo", "Ida", "Milan",
         "Sara", "Jonas", "Lea", "Erik", "Mira", "Oscar", "Elena", "Felix", "Nora", "Adam"];

    private static readonly string[] LastNames =
        ["Kovač", "Novak", "Weber", "Rossi", "Silva", "Petrov", "Nagy", "Dubois", "Fischer", "Horvat"];

    private static readonly string[] JobTitles =
        ["Operator", "Team Lead", "Technician", "Warehouse Clerk", "Quality Inspector",
         "Shift Supervisor", "Maintenance Engineer", "Logistics Planner"];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Sites.AnyAsync(ct))
            return;

        var hq = new Site { Name = "Headquarters", TimeZone = "Europe/Ljubljana" };
        var plant = new Site { Name = "Production Plant", ParentId = hq.Id, TimeZone = "Europe/Ljubljana" };
        var warehouse = new Site { Name = "Warehouse North", ParentId = hq.Id, TimeZone = "Europe/Ljubljana" };
        db.Sites.AddRange(hq, plant, warehouse);

        var departments = new[]
        {
            new Department { Name = "Assembly", SiteId = plant.Id },
            new Department { Name = "Quality", SiteId = plant.Id },
            new Department { Name = "Inbound", SiteId = warehouse.Id },
            new Department { Name = "Outbound", SiteId = warehouse.Id },
            new Department { Name = "Administration", SiteId = hq.Id },
        };
        db.Departments.AddRange(departments);

        var rng = new Random(20260720); // fixed seed → same demo data everywhere
        var sites = new[] { hq, plant, plant, plant, warehouse, warehouse }; // weighted
        for (var i = 0; i < 40; i++)
        {
            var site = sites[rng.Next(sites.Length)];
            var siteDepartments = departments.Where(d => d.SiteId == site.Id).ToArray();
            var first = FirstNames[rng.Next(FirstNames.Length)];
            var last = LastNames[rng.Next(LastNames.Length)];
            db.Employees.Add(new Employee
            {
                Code = $"E{1000 + i}",
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}@wm.demo".ToLowerInvariant().Replace("č", "c").Replace("š", "s"),
                JobTitle = JobTitles[rng.Next(JobTitles.Length)],
                SiteId = site.Id,
                DepartmentId = siteDepartments.Length > 0 ? siteDepartments[rng.Next(siteDepartments.Length)].Id : null,
                EmployedFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-rng.Next(60, 2500))),
            });
        }

        // No leaving reasons are seeded, deliberately: they are customer vocabulary, and a WM-invented
        // list ("Resignation", "Redundancy") would be indistinguishable from a shipped default that
        // every install then has to curate. Every seeded employee is employed and open-ended.
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo sites, departments and 40 employees");
    }
}
