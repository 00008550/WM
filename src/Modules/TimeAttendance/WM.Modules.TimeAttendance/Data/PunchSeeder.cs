using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Data;

/// <summary>
/// Seeds a believable "today" — most employees clocked in this morning, a few out.
///
/// <para>
/// 008 P4: the morning is the morning <b>at the employee's home site</b>, and the days are their
/// local days. Before P4 every punch was laid on a UTC day at 07:00 UTC, which a Ljubljana timesheet
/// then showed at 09:00, and an evening clock-out after 22:00 UTC on the wrong date.
/// </para>
/// </summary>
public sealed class PunchSeeder(
    TimeAttendanceDbContext db,
    IEmployeeDirectory employees,
    ISiteTimeZones zones,
    IOwningDayResolver owningDay,
    IClock clock,
    ILogger<PunchSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Punches.AnyAsync(ct))
            return;

        var rng = new Random(20260720);
        var now = clock.UtcNow;

        // Seeding runs at startup with no signed-in user, so it must bypass data scope. Employment is
        // asked as at each employee's local today; the five days of history below are seeded for
        // whoever is employed now, which is every seeded employee.
        var active = await employees.ListEmployedAtLocalTodayUnscopedAsync(ct);
        if (active.Count == 0)
            return;

        var zoneBySite = await zones.ForSitesAsync(active.Select(e => e.SiteId).Distinct(), ct);
        foreach (var employee in active)
        {
            var zone = zoneBySite[employee.SiteId].Zone;
            var today = clock.TodayIn(zone);

            // Last 5 working days of history.
            for (var d = 5; d >= 1; d--)
            {
                var day = today.AddDays(-d);
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var clockIn = At(day, new TimeOnly(7, 0).AddMinutes(rng.Next(0, 75)), zone);
                var clockOut = clockIn.AddHours(7.5 + rng.NextDouble() * 2);
                AddPair(employee, zone, clockIn, clockOut, now, rng);
            }

            // Today: ~75% are in; a third of those already left. Nobody is clocked in from the future.
            if (rng.NextDouble() < 0.75)
            {
                var clockIn = At(today, new TimeOnly(7, 0).AddMinutes(rng.Next(0, 90)), zone);
                if (clockIn > now) continue;
                DateTimeOffset? clockOut = rng.NextDouble() < 0.3 ? clockIn.AddHours(6 + rng.NextDouble() * 3) : null;
                AddPair(employee, zone, clockIn, clockOut, now, rng);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo punches for {Count} employees", active.Count);
    }

    /// <summary>A wall-clock time on a local day, as an instant. Morning times never fall in a DST gap.</summary>
    private static DateTimeOffset At(DateOnly day, TimeOnly time, ZoneId zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.Info.GetUtcOffset(local));
    }

    private void AddPair(
        EmployeeSummary employee, ZoneId zone, DateTimeOffset clockIn, DateTimeOffset? clockOut,
        DateTimeOffset now, Random rng)
    {
        db.Punches.Add(NewPunch(employee, zone, clockIn, PunchDirection.In, rng));
        if (clockOut.HasValue && clockOut.Value < now)
            db.Punches.Add(NewPunch(employee, zone, clockOut.Value, PunchDirection.Out, rng));
    }

    private Punch NewPunch(
        EmployeeSummary employee, ZoneId zone, DateTimeOffset timestamp, PunchDirection direction, Random rng) => new()
    {
        EmployeeId = employee.Id,
        EmployeeCode = employee.Code,
        Timestamp = timestamp.ToUniversalTime(),
        Direction = direction,
        Source = PunchSource.Terminal,
        DeviceId = $"TERM-{rng.Next(1, 4):00}",
        SiteId = employee.SiteId,
        LocalDate = owningDay.Resolve(timestamp, zone),
        LocalZone = zone.Id,
        ReceivedAt = timestamp.ToUniversalTime(), // a terminal punch, stamped as it happened
    };
}
