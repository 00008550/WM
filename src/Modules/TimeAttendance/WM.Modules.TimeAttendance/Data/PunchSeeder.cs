using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Domain;

namespace WM.Modules.TimeAttendance.Data;

/// <summary>Seeds a believable "today" — most employees clocked in this morning, a few out.</summary>
public sealed class PunchSeeder(
    TimeAttendanceDbContext db,
    IEmployeeDirectory employees,
    ILogger<PunchSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Punches.AnyAsync(ct))
            return;

        var rng = new Random(20260720);
        var today = DateTimeOffset.UtcNow.Date;

        // Seeding runs at startup with no signed-in user, so it must bypass data scope. Employment is
        // asked as at today; the five days of history below are seeded for whoever is employed now,
        // which is every seeded employee.
        var active = await employees.ListEmployedOnUnscopedAsync(DateOnly.FromDateTime(today), ct);
        if (active.Count == 0)
            return;

        foreach (var employee in active)
        {
            // Last 5 working days of history.
            for (var d = 5; d >= 1; d--)
            {
                var day = today.AddDays(-d);
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var clockIn = day.AddHours(7).AddMinutes(rng.Next(0, 75));
                var clockOut = clockIn.AddHours(7.5 + rng.NextDouble() * 2);
                AddPair(employee, clockIn, clockOut, rng);
            }

            // Today: ~75% are in; a third of those already left.
            if (rng.NextDouble() < 0.75)
            {
                var clockIn = today.AddHours(7).AddMinutes(rng.Next(0, 90));
                DateTime? clockOut = rng.NextDouble() < 0.3 ? clockIn.AddHours(6 + rng.NextDouble() * 3) : null;
                AddPair(employee, clockIn, clockOut, rng);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo punches for {Count} employees", active.Count);
    }

    private void AddPair(EmployeeSummary employee, DateTime clockIn, DateTime? clockOut, Random rng)
    {
        db.Punches.Add(NewPunch(employee, clockIn, PunchDirection.In, rng));
        if (clockOut.HasValue && clockOut.Value < DateTime.UtcNow)
            db.Punches.Add(NewPunch(employee, clockOut.Value, PunchDirection.Out, rng));
    }

    private static Punch NewPunch(EmployeeSummary employee, DateTime timestamp, PunchDirection direction, Random rng) => new()
    {
        EmployeeId = employee.Id,
        EmployeeCode = employee.Code,
        Timestamp = new DateTimeOffset(timestamp, TimeSpan.Zero),
        Direction = direction,
        Source = PunchSource.Terminal,
        DeviceId = $"TERM-{rng.Next(1, 4):00}",
        SiteId = employee.SiteId,
    };
}
