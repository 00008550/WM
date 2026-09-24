using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Modules.TimeAttendance.Tests.Services;

/// <summary>
/// 008 P4 — a punch belongs to a <b>local</b> day: the calendar date on the wall clock of the
/// employee's home-site zone, resolved once when the punch is recorded and frozen on the row
/// (open question 4 = freeze). The timesheet groups by that frozen date and never re-derives it.
/// Every instant here is chosen so UTC's date disagrees with the local one.
/// </summary>
public sealed class LocalDayTests
{
    private static readonly Guid EmployeeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000008");
    private static readonly Guid SiteId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000008");
    private const string Code = "E0008";

    private static readonly ZoneId Tashkent = ZoneId.Parse("Asia/Tashkent");
    private static readonly ZoneId LosAngeles = ZoneId.Parse("America/Los_Angeles");
    private static readonly ZoneId Ljubljana = ZoneId.Parse("Europe/Ljubljana");

    /// <summary>"Now" for every test: after all the punches, so the future guard never interferes.</summary>
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static EmployeeSummary Employee(DateOnly? until = null) =>
        new(EmployeeId, Code, "Ada Lovelace", null, SiteId, null, new DateOnly(2020, 1, 1), until, false);

    private sealed record Rig(PunchService Service, TimeAttendanceDbContext Db, FixedZones Zones);

    private static Rig Build(ZoneId zone, EmployeeSummary? employee = null, DateTimeOffset? now = null)
    {
        var db = new TimeAttendanceDbContext(new DbContextOptionsBuilder<TimeAttendanceDbContext>()
            .UseInMemoryDatabase($"local-day-{Guid.NewGuid()}").Options);
        var zones = new FixedZones(zone);
        var service = new PunchService(db, new StubDirectory(employee ?? Employee()), new NoopEventStream(),
            Options.Create(new PunchDeduplicationOptions()), Options.Create(new PunchTimingOptions()), zones, new LocalCalendarDayResolver(), new PunchBackedClockingDays(db),
            new SystemClock(new FixedTime(now ?? Now)));
        return new Rig(service, db, zones);
    }

    private static async Task<Punch> Record(Rig rig, DateTimeOffset at, PunchDirection direction = PunchDirection.In)
    {
        var result = await rig.Service.RecordAsync(new RecordPunchRequest(Code, direction, PunchSource.Web, at.ToString("O")), null, default);
        Assert.Null(result.Error);
        return result.Punch!;
    }

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ---- the day boundary, in both directions ----

    [Fact]
    public async Task A_2330_UTC_punch_at_Tashkent_lands_on_the_next_local_day()
    {
        var rig = Build(Tashkent);
        var punch = await Record(rig, new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(D(2026, 3, 11), punch.LocalDate);
        Assert.Equal("Asia/Tashkent", punch.LocalZone);
        var day = Assert.Single(await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 3, 9), D(2026, 3, 12), default));
        Assert.Equal(D(2026, 3, 11), day.Date);
    }

    [Fact]
    public async Task A_0030_UTC_punch_at_Los_Angeles_lands_on_the_previous_local_day()
    {
        var rig = Build(LosAngeles);
        var punch = await Record(rig, new DateTimeOffset(2026, 1, 20, 0, 30, 0, TimeSpan.Zero));

        Assert.Equal(D(2026, 1, 19), punch.LocalDate);
        var day = Assert.Single(await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 1, 18), D(2026, 1, 21), default));
        Assert.Equal(D(2026, 1, 19), day.Date);
    }

    [Fact]
    public async Task A_Ljubljana_punch_at_2230_UTC_in_July_is_on_the_23rd_not_the_22nd()
    {
        // The seeded-demo defect, pinned: CEST is +02, so 22:30 UTC on the 22nd is 00:30 on the 23rd.
        var rig = Build(Ljubljana);
        await Record(rig, new DateTimeOffset(2026, 7, 22, 22, 30, 0, TimeSpan.Zero));

        var days = await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 7, 22), D(2026, 7, 23), default);

        Assert.Equal(D(2026, 7, 23), Assert.Single(days).Date);
    }

    [Fact]
    public async Task A_range_of_local_days_excludes_a_punch_whose_UTC_date_is_inside_it()
    {
        // 23:30 UTC on the 10th is the 11th in Tashkent: a timesheet for the 10th alone is empty.
        var rig = Build(Tashkent);
        await Record(rig, new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero));

        Assert.Empty(await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 3, 10), D(2026, 3, 10), default));
    }

    // ---- freeze (open question 4) ----

    [Fact]
    public async Task A_punch_keeps_its_local_day_after_its_sites_zone_is_edited()
    {
        var rig = Build(Tashkent);
        await Record(rig, new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero)); // the 11th in Tashkent

        // An administrator moves the site to Los Angeles, where that instant is 16:30 on the 10th.
        rig.Zones.Zone = LosAngeles;

        var days = await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 3, 9), D(2026, 3, 12), default);
        var day = Assert.Single(days);
        Assert.Equal(D(2026, 3, 11), day.Date);
        Assert.Equal("Asia/Tashkent", (await rig.Db.Punches.SingleAsync()).LocalZone);
    }

    // ---- DST ----

    [Fact]
    public async Task Fall_back_two_punches_an_hour_apart_both_at_local_0130_stay_two_punches_on_one_day_in_order()
    {
        // 2026-11-01, Los Angeles: 01:30 PDT (08:30 UTC), then clocks go back, then 01:30 PST (09:30 UTC).
        var rig = Build(LosAngeles);
        var first = await Record(rig, new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero), PunchDirection.In);
        var second = await Record(rig, new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), PunchDirection.Out);

        foreach (var punch in new[] { first, second })
            Assert.Equal(new TimeOnly(1, 30), TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(punch.Timestamp, LosAngeles.Info).DateTime));
        Assert.NotEqual(first.Id, second.Id);

        var day = Assert.Single(await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 11, 1), D(2026, 11, 1), default));
        var interval = Assert.Single(day.Intervals);
        Assert.Equal(first.Timestamp, interval.In);
        Assert.Equal(second.Timestamp, interval.Out);
        Assert.Equal(1.0, interval.Hours);
    }

    [Fact]
    public async Task Spring_forward_a_23_hour_local_day_holds_its_first_and_last_hour_and_loses_nothing()
    {
        // 2026-03-08, Los Angeles, 23 hours long: 00:30 PST is 08:30 UTC; 23:30 PDT is 06:30 UTC on
        // the 9th. A +24h window from UTC midnight — or UTC grouping — splits them across two days.
        var rig = Build(LosAngeles);
        await Record(rig, new DateTimeOffset(2026, 3, 8, 8, 30, 0, TimeSpan.Zero), PunchDirection.In);
        await Record(rig, new DateTimeOffset(2026, 3, 9, 6, 30, 0, TimeSpan.Zero), PunchDirection.Out);

        var day = Assert.Single(await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 3, 7), D(2026, 3, 9), default));
        Assert.Equal(D(2026, 3, 8), day.Date);
        Assert.Equal(22.0, Assert.Single(day.Intervals).Hours);
    }

    // ---- no regression for a single-zone UTC install ----

    [Fact]
    public async Task A_zone_that_resolves_to_UTC_behaves_exactly_as_before()
    {
        var rig = Build(ZoneId.Utc);
        await Record(rig, new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero), PunchDirection.In);
        await Record(rig, new DateTimeOffset(2026, 3, 11, 0, 30, 0, TimeSpan.Zero), PunchDirection.Out);

        var days = await rig.Service.GetTimesheetAsync(EmployeeId, D(2026, 3, 10), D(2026, 3, 11), default);

        Assert.Equal([D(2026, 3, 10), D(2026, 3, 11)], days.Select(d => d.Date));
    }

    // ---- the employment check reads the same local day ----

    [Fact]
    public async Task A_punch_on_the_local_day_after_the_leave_date_is_refused_though_UTC_is_still_the_last_day()
    {
        var rig = Build(Tashkent, Employee(until: D(2026, 3, 10)));
        var result = await rig.Service.RecordAsync(new RecordPunchRequest(Code, PunchDirection.In, PunchSource.Web,
            new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero).ToString("O")), null, default);

        Assert.Null(result.Punch);
    }

    // ---- default "today" ----

    [Fact]
    public async Task The_timesheets_default_today_is_the_employees_local_today()
    {
        // 12:00 UTC on 15 Jan is already the 16th in Auckland (+13).
        var rig = Build(ZoneId.Parse("Pacific/Auckland"), now: new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(D(2026, 1, 16), await rig.Service.LocalTodayAsync(Employee(), default));
    }

    // ---- the backfill of punches recorded before P4 ----

    [Fact]
    public async Task The_backfill_freezes_every_unfrozen_punch_in_its_sites_zone_and_leaves_frozen_ones_alone()
    {
        var rig = Build(Tashkent);
        rig.Db.Punches.AddRange(
            Legacy(new DateTimeOffset(2026, 3, 10, 18, 0, 0, TimeSpan.Zero)),  // 23:00 on the 10th
            Legacy(new DateTimeOffset(2026, 3, 10, 19, 30, 0, TimeSpan.Zero)), // 00:30 on the 11th
            Legacy(new DateTimeOffset(2026, 3, 11, 0, 30, 0, TimeSpan.Zero)),  // 05:30 on the 11th
            new Punch
            {
                EmployeeId = EmployeeId, EmployeeCode = Code, SiteId = SiteId,
                Timestamp = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero),
                LocalDate = D(2026, 3, 9), LocalZone = "Pacific/Honolulu", // already frozen — must not move
            });
        await rig.Db.SaveChangesAsync();

        var backfill = new PunchLocalDateBackfill(rig.Db, rig.Zones,
            NullLogger<PunchLocalDateBackfill>.Instance);

        Assert.Equal(3, await backfill.RunAsync());
        Assert.Equal(0, await backfill.RunAsync());

        var rows = await rig.Db.Punches.AsNoTracking().OrderBy(p => p.Timestamp).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal([D(2026, 3, 9), D(2026, 3, 10), D(2026, 3, 11), D(2026, 3, 11)], rows.Select(p => p.LocalDate!.Value));
        Assert.Equal(["Pacific/Honolulu", "Asia/Tashkent", "Asia/Tashkent", "Asia/Tashkent"], rows.Select(p => p.LocalZone!));
    }

    private static Punch Legacy(DateTimeOffset at) => new()
    {
        EmployeeId = EmployeeId, EmployeeCode = Code, SiteId = SiteId, Timestamp = at,
        Direction = PunchDirection.In, Source = PunchSource.Terminal,
    };
}
