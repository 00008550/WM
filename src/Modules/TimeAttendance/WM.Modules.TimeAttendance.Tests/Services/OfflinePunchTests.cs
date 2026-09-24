using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Tests.Services;

/// <summary>
/// 008 P5 — an offline punch carries its own offset. A client timestamp without one is refused; a
/// queued punch from the past is accepted and flagged; the server's receipt instant is stored beside
/// the client's; and the punch's day comes from its own instant, never from when it arrived.
/// </summary>
public sealed class OfflinePunchTests
{
    private static readonly Guid EmployeeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid SiteId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005");
    private const string Code = "E0005";

    private static readonly ZoneId Tashkent = ZoneId.Parse("Asia/Tashkent");     // +05, no DST
    private static readonly ZoneId Ljubljana = ZoneId.Parse("Europe/Ljubljana"); // +01 / +02

    /// <summary>The server's clock: 25 Sep 2026, 10:00 UTC (15:00 in Tashkent).</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static (PunchService Service, TimeAttendanceDbContext Db) Build(ZoneId zone, TimeSpan? lateAfter = null)
    {
        var db = new TimeAttendanceDbContext(new DbContextOptionsBuilder<TimeAttendanceDbContext>()
            .UseInMemoryDatabase($"offline-{Guid.NewGuid()}").Options);
        var employee = new EmployeeSummary(EmployeeId, Code, "Ada Lovelace", null, SiteId, null,
            new DateOnly(2020, 1, 1), null, false);
        var timing = new PunchTimingOptions();
        if (lateAfter is { } threshold) timing.LateAfter = threshold;
        var service = new PunchService(db, new StubDirectory(employee), new NoopEventStream(),
            Options.Create(new PunchDeduplicationOptions()), Options.Create(timing),
            new FixedZones(zone), new LocalCalendarDayResolver(), new SystemClock(new FixedTime(Now)));
        return (service, db);
    }

    private static Task<PunchResult> Record(PunchService service, string? timestamp) =>
        service.RecordAsync(new RecordPunchRequest(Code, PunchDirection.In, PunchSource.Mobile, timestamp), null, default);

    // ---- rejected: a timestamp without an offset ----

    [Theory]
    [InlineData("2026-09-25T08:00:00")]
    [InlineData("2026-09-25T08:00:00.123")]
    [InlineData("2026-09-25 08:00:00")]
    [InlineData("25/09/2026 08:00")]
    [InlineData("2026-09-25")]
    public async Task A_timestamp_without_an_offset_is_rejected_and_nothing_is_stored(string timestamp)
    {
        var (service, db) = Build(Tashkent);

        var result = await Record(service, timestamp);

        Assert.Null(result.Punch);
        Assert.Contains("offset", result.Error);
        Assert.Equal(0, await db.Punches.CountAsync());
    }

    [Theory]
    [InlineData("2026-09-25T08:00:00Z")]
    [InlineData("2026-09-25T13:00:00+05:00")]
    public async Task A_timestamp_with_an_offset_is_accepted_as_that_instant(string timestamp)
    {
        var (service, _) = Build(Tashkent);

        var result = await Record(service, timestamp);

        Assert.Null(result.Error);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero), result.Punch!.Timestamp);
    }

    // ---- the future stays refused ----

    [Fact]
    public async Task A_punch_ten_minutes_in_the_future_is_rejected_as_today()
    {
        var (service, db) = Build(Tashkent);

        var result = await Record(service, Now.AddMinutes(10).ToString("O"));

        Assert.Null(result.Punch);
        Assert.Equal("Punch timestamp cannot be in the future.", result.Error);
        Assert.Equal(0, await db.Punches.CountAsync());
    }

    // ---- accepted and flagged: the past ----

    [Fact]
    public async Task A_queued_punch_thirty_hours_old_is_accepted_and_flagged_late()
    {
        var (service, db) = Build(Tashkent);

        var result = await Record(service, Now.AddHours(-30).ToOffset(TimeSpan.FromHours(5)).ToString("O"));

        Assert.NotNull(result.Punch);
        Assert.True(result.Punch!.Flags.HasFlag(PunchFlags.Late));
        Assert.Equal(1, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A_punch_inside_the_late_threshold_is_not_flagged()
    {
        var (service, _) = Build(Tashkent);

        var result = await Record(service, Now.AddMinutes(-30).ToOffset(TimeSpan.FromHours(5)).ToString("O"));

        Assert.Equal(PunchFlags.None, result.Punch!.Flags);
    }

    [Fact]
    public async Task The_late_threshold_is_configuration()
    {
        var (service, _) = Build(Tashkent, lateAfter: TimeSpan.FromMinutes(10));

        var result = await Record(service, Now.AddMinutes(-30).ToOffset(TimeSpan.FromHours(5)).ToString("O"));

        Assert.True(result.Punch!.Flags.HasFlag(PunchFlags.Late));
    }

    [Fact]
    public void A_non_positive_late_threshold_is_a_composition_fault()
    {
        Assert.NotNull(PunchTimingOptions.DescribeFault(new PunchTimingOptions { LateAfter = TimeSpan.Zero }));
        Assert.Null(PunchTimingOptions.DescribeFault(new PunchTimingOptions()));
    }

    // ---- both clocks are kept ----

    [Fact]
    public async Task Client_and_server_instants_are_both_persisted_and_both_surface_on_the_punch_dto()
    {
        var (service, db) = Build(Tashkent);
        var client = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5));

        await Record(service, client.ToString("O"));

        var stored = await db.Punches.SingleAsync();
        Assert.Equal(client, stored.Timestamp);
        Assert.Equal(Now, stored.ReceivedAt);
        Assert.Equal(300, stored.ClientUtcOffsetMinutes);

        var row = Assert.Single(await service.GetRecentForEmployeeAsync(EmployeeId, 10, default));
        Assert.Equal(client, row.Timestamp);
        Assert.Equal(Now, row.ReceivedAt);
        Assert.True(row.Flags.HasFlag(PunchFlags.Late));
    }

    [Fact]
    public async Task A_server_stamped_punch_has_equal_instants_no_client_offset_and_no_flags()
    {
        var (service, db) = Build(Tashkent);

        await Record(service, null);

        var stored = await db.Punches.SingleAsync();
        Assert.Equal(Now, stored.Timestamp);
        Assert.Equal(Now, stored.ReceivedAt);
        Assert.Null(stored.ClientUtcOffsetMinutes);
        Assert.Equal(PunchFlags.None, stored.Flags);
    }

    // ---- the day belongs to the punch, not to its arrival ----

    [Fact]
    public async Task A_queued_punch_lands_on_its_own_local_day_not_the_day_it_arrived()
    {
        // 23:30 on 23 Sep in Tashkent, received at 15:00 on 25 Sep: its day is the 23rd.
        var (service, db) = Build(Tashkent);

        await Record(service, "2026-09-23T23:30:00+05:00");

        var stored = await db.Punches.SingleAsync();
        Assert.Equal(new DateOnly(2026, 9, 23), stored.LocalDate);
        var day = Assert.Single(await service.GetTimesheetAsync(EmployeeId, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 25), default));
        Assert.Equal(new DateOnly(2026, 9, 23), day.Date);
    }

    // ---- DST spring-forward: a wall clock that never existed ----

    [Fact]
    public async Task Spring_forward_a_queued_local_0230_with_its_offset_is_stored_as_that_instant_not_rejected()
    {
        // Decision (008 P5, the plan's spring-forward edge case: "rejected or normalised"): NORMALISED.
        // Ljubljana skipped 02:00-03:00 on 29 Mar 2026, so local 02:30 never happened — but with the
        // offset required, "02:30+01:00" is not a wall clock, it is one unambiguous instant (01:30Z,
        // which Ljubljana called 03:30+02:00). It is stored as that instant, on local 29 March, and
        // not flagged: +01:00 is Ljubljana's standard offset, within the DST shift in force that day
        // (a phone that has not switched yet). Nothing is silently shifted — the client's instant is kept.
        // LateAfter widened: "now" is September, and this test is about the offset, not queue age.
        var (service, db) = Build(Ljubljana, lateAfter: TimeSpan.FromDays(365));

        var result = await Record(service, "2026-03-29T02:30:00+01:00");

        Assert.Null(result.Error);
        var stored = await db.Punches.SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), stored.Timestamp);
        Assert.Equal(new DateOnly(2026, 3, 29), stored.LocalDate);
        Assert.Equal(PunchFlags.None, stored.Flags);
    }

    // ---- the offset against the home site's zone ----

    [Fact]
    public async Task An_offset_contradicting_the_home_zone_beyond_its_DST_shift_is_flagged_not_dropped()
    {
        // Ljubljana in September is +02; a device on +05 is not a DST lag, it is elsewhere.
        var (service, db) = Build(Ljubljana);

        var result = await Record(service, "2026-09-25T14:30:00+05:00");

        Assert.NotNull(result.Punch);
        Assert.True(result.Punch!.Flags.HasFlag(PunchFlags.OffsetMismatch));
        Assert.Equal(1, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A_device_one_DST_shift_behind_its_home_zone_is_tolerated()
    {
        // +01 in Ljubljana's summer: a phone that has not switched yet. Within the zone's DST shift.
        var (service, _) = Build(Ljubljana);

        var result = await Record(service, "2026-09-25T10:30:00+01:00");

        Assert.False(result.Punch!.Flags.HasFlag(PunchFlags.OffsetMismatch));
    }

    [Fact]
    public async Task A_zone_without_DST_tolerates_no_offset_difference()
    {
        var (service, _) = Build(Tashkent);

        var result = await Record(service, "2026-09-25T13:30:00+04:00");

        Assert.True(result.Punch!.Flags.HasFlag(PunchFlags.OffsetMismatch));
    }

    [Fact]
    public async Task Z_is_compared_like_any_offset_so_a_client_must_send_its_local_one()
    {
        var (service, _) = Build(Tashkent);

        var result = await Record(service, "2026-09-25T09:30:00Z");

        // Z is the instant in UTC notation; it says nothing about the device's zone, and is compared
        // like any other offset: 0 against Tashkent's +05 is a mismatch.
        Assert.True(result.Punch!.Flags.HasFlag(PunchFlags.OffsetMismatch));

        var (service2, _) = Build(Tashkent);
        var matching = await Record(service2, "2026-09-25T14:30:00+05:00");
        Assert.Equal(PunchFlags.None, matching.Punch!.Flags);
    }
}
