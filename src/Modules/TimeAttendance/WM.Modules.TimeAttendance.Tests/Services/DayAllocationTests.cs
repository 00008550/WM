using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Modules.TimeAttendance.Tests.Services;

/// <summary>
/// A Clocking store double (plan 010 P2): plan 002 has not landed, so allocation's view of the days is
/// supplied here. Punches are kept in insertion order — deliberately not time order (edge case A8).
/// </summary>
internal sealed class FakeClockingDays : IClockingDays
{
    private readonly Dictionary<DateOnly, ClockingDay> _days = [];
    public List<DateOnly> Created { get; } = [];

    public FakeClockingDays Day(DateOnly date, DayTemplate? template, params DateTimeOffset[] punches)
    {
        _days[date] = new ClockingDay(date, template?.Id, punches);
        return this;
    }

    public Task<ClockingDay?> FindAsync(Guid employeeId, DateOnly date, CancellationToken ct) =>
        Task.FromResult(_days.GetValueOrDefault(date));

    public Task<bool> EnsureAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        if (_days.ContainsKey(date)) return Task.FromResult(false);
        _days[date] = new ClockingDay(date, null, []);
        Created.Add(date);
        return Task.FromResult(true);
    }
}

/// <summary>
/// Plan 010 P2 — swipe → day allocation, ported from <c>dbo.ProcessQueryGetClockingForSwipe</c>
/// (<c>37.V3.6.1.0.sql:748-868</c>). A test per branch, its boundary at exactly the configured time and
/// one tick inside it, the order between branches, edge cases A4, A5, A7, A8, A9, A10, and night shifts
/// across local midnight and DST. Templates go through the real <see cref="DayTemplateDirectory"/>.
/// </summary>
public sealed class DayAllocationTests
{
    private static readonly Guid EmployeeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000102");

    // The vault's own dates (Documentation\Swipe to clocking allocation.md:43-118).
    private static readonly DateOnly Mar1 = new(2023, 3, 1);
    private static readonly DateOnly Mar2 = new(2023, 3, 2);
    private static readonly DateOnly Mar3 = new(2023, 3, 3);

    private static readonly ZoneId Ljubljana = ZoneId.Parse("Europe/Ljubljana");
    private static readonly ZoneId Tashkent = ZoneId.Parse("Asia/Tashkent");

    private sealed class Rig
    {
        public TimeAttendanceDbContext Db { get; } = new(new DbContextOptionsBuilder<TimeAttendanceDbContext>()
            .UseInMemoryDatabase($"allocation-{Guid.NewGuid()}").Options);
        public FakeClockingDays Days { get; } = new();
        public DayAllocationService Service => new(Days, new DayTemplateDirectory(Db));

        public DayTemplate Template(Action<DayTemplate> configure)
        {
            var t = new DayTemplate { Code = $"T{Db.DayTemplates.Local.Count}", Name = "t", Kind = TemplateKind.FixedSchedule1Range };
            configure(t);
            Db.DayTemplates.Add(t);
            Db.SaveChanges();
            return t;
        }

        public Task<OwningDay> Resolve(DateTimeOffset instant, ZoneId? zone = null) =>
            Service.ResolveAsync(EmployeeId, instant, zone ?? ZoneId.Utc, default);
    }

    /// <summary>A wall-clock time on a date in <paramref name="zone"/>, as an instant (UTC by default).</summary>
    private static DateTimeOffset At(DateOnly date, int h, int m = 0, int s = 0, ZoneId? zone = null)
    {
        var local = date.ToDateTime(new TimeOnly(h, m, s), DateTimeKind.Unspecified);
        var info = (zone ?? ZoneId.Utc).Info;
        return new DateTimeOffset(local, info.GetUtcOffset(local));
    }

    private static TimeOnly T(int h, int m = 0) => new(h, m);

    // ================= Branch 1 — NightShiftEndTime (:774-785) =================

    [Fact]
    public async Task Branch1_a_punch_before_yesterdays_night_shift_end_belongs_to_yesterday()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.NightShiftEndTime = T(4)));

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.NightShiftEnd), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task Branch1_A4_a_punch_at_exactly_night_shift_end_stays_on_its_own_day_and_one_second_before_does_not()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.NightShiftEndTime = T(4)));

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 4)));
        Assert.Equal(new OwningDay(Mar1, AllocationBranch.NightShiftEnd), await rig.Resolve(At(Mar2, 3, 59, 59)));
    }

    [Fact]
    public async Task Branch1_needs_yesterday_to_have_a_clocking_with_a_template()
    {
        var rig = new Rig();
        var nightTemplate = rig.Template(t => t.NightShiftEndTime = T(4));
        rig.Days.Day(Mar2, nightTemplate); // today's template is not yesterday's

        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 2))).Date);
        rig.Days.Day(Mar1, null);          // yesterday exists, no template
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 2))).Date);
    }

    [Fact]
    public async Task A_neighbour_whose_template_id_no_longer_exists_offers_nothing()
    {
        var rig = new Rig();
        var dangling = new DayTemplate { Code = "GONE", Name = "gone", NightShiftEndTime = T(4) }; // never saved
        rig.Days.Day(Mar1, dangling);

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 2)));
    }

    // ================= Branch 2 — OffsetTransactionToNextDay + OffsetAfterTime (:787-799) =================

    [Fact]
    public async Task Branch2_a_punch_after_tomorrows_armed_offset_time_belongs_to_tomorrow()
    {
        var rig = new Rig();
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(22); }));

        Assert.Equal(new OwningDay(Mar3, AllocationBranch.OffsetToNextDay), await rig.Resolve(At(Mar2, 22, 30)));
    }

    [Fact]
    public async Task Branch2_A5_a_punch_at_exactly_the_offset_time_stays_on_its_own_day_and_one_second_after_does_not()
    {
        var rig = new Rig();
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(22); }));

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 22)));
        Assert.Equal(new OwningDay(Mar3, AllocationBranch.OffsetToNextDay), await rig.Resolve(At(Mar2, 22, 0, 1)));
    }

    [Fact]
    public async Task Branch2_A6_the_time_without_the_switch_does_nothing_and_the_switch_without_a_time_does_nothing()
    {
        var rig = new Rig();
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = false; t.OffsetAfterTime = T(22); }));
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 23))).Date);

        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = null; }));
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 23))).Date);
    }

    [Fact]
    public async Task Branch2_reads_tomorrows_template_not_todays()
    {
        var rig = new Rig();
        rig.Days.Day(Mar2, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(22); }));

        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 23))).Date);
    }

    // ================= Branch 3 — AllocateToPreviousDayWindow (:801-834) =================

    [Fact]
    public async Task Branch3_with_nothing_today_a_punch_within_the_window_of_yesterdays_first_punch_belongs_to_yesterday()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), At(Mar1, 18));

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.AllocateToPreviousDay), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task Branch3_boundary_first_punch_plus_window_exactly_stays_on_its_own_day_one_second_before_does_not()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), At(Mar1, 18));

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 3)));
        Assert.Equal(new OwningDay(Mar1, AllocationBranch.AllocateToPreviousDay), await rig.Resolve(At(Mar2, 2, 59, 59)));
    }

    [Fact]
    public async Task Branch3_does_not_fire_once_today_already_has_a_punch()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), At(Mar1, 18));
        rig.Days.Day(Mar2, null, At(Mar2, 1));

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task Branch3_an_existing_but_empty_today_does_not_block_it()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), At(Mar1, 18));
        rig.Days.Day(Mar2, null); // generated by the calendar, no punches yet

        Assert.Equal(Mar1, (await rig.Resolve(At(Mar2, 2))).Date);
    }

    [Fact]
    public async Task Branch3_A8_inverted_anchors_on_yesterdays_earliest_punch_not_its_first_stored_slot()
    {
        var rig = new Rig();
        // Stored out of time order: slot 1 holds 22:00, slot 2 holds 18:00.
        DateTimeOffset[] slots = [At(Mar1, 22), At(Mar1, 18)];
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), slots);
        var punch = At(Mar2, 5);

        // Legacy (:823-825): isnull(badgetime1, badgetime2, ...) = 22:00; 22:00 + 9h = 07:00 > 05:00 → yesterday.
        var legacySaysYesterday = slots[0] + TimeSpan.FromHours(9) > punch;
        Assert.True(legacySaysYesterday);

        // WM: earliest = 18:00; 18:00 + 9h = 03:00, not > 05:00 → the punch's own day.
        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(punch));
    }

    // ================= Branch 5 — shift matching (:836-864), edge case A7 =================

    private static DayTemplate ShiftMatching(Rig rig, ShiftMatchType type, Guid target,
        TimeOnly? startFrom = null, TimeOnly? startTo = null, TimeOnly? endFrom = null, TimeOnly? endTo = null) =>
        rig.Template(t =>
        {
            t.Kind = TemplateKind.ShiftMatching;
            t.ShiftMatchingRules.Add(new ShiftMatchingRule
            {
                DayTemplateId = t.Id, MatchType = type, TemplateToAssignId = target,
                StartTimeFrom = startFrom, StartTimeTo = startTo, EndTimeFrom = endFrom, EndTimeTo = endTo,
            });
        });

    [Fact]
    public async Task Branch5_A7_end_only_a_punch_completing_a_rule_whose_target_ends_after_it_belongs_to_yesterday()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        rig.Days.Day(Mar1, ShiftMatching(rig, ShiftMatchType.EndOnly, night.Id, endFrom: T(5), endTo: T(7)), At(Mar1, 22));

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.ShiftMatching), await rig.Resolve(At(Mar2, 6)));
    }

    [Fact]
    public async Task Branch5_end_window_bounds_are_inclusive()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        rig.Days.Day(Mar1, ShiftMatching(rig, ShiftMatchType.EndOnly, night.Id, endFrom: T(5), endTo: T(7)), At(Mar1, 22));

        Assert.Equal(Mar1, (await rig.Resolve(At(Mar2, 5))).Date);
        Assert.Equal(Mar1, (await rig.Resolve(At(Mar2, 7))).Date);
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 4, 59, 59))).Date);
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 7, 0, 1))).Date);
    }

    [Fact]
    public async Task Branch5_the_targets_night_shift_end_is_strict()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(6));
        rig.Days.Day(Mar1, ShiftMatching(rig, ShiftMatchType.EndOnly, night.Id, endFrom: T(5), endTo: T(7)), At(Mar1, 22));

        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 6))).Date);
        Assert.Equal(Mar1, (await rig.Resolve(At(Mar2, 5, 59, 59))).Date);
    }

    [Fact]
    public async Task Branch5_start_and_end_needs_yesterdays_first_punch_in_the_start_window_too()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        var matching = ShiftMatching(rig, ShiftMatchType.StartAndEnd, night.Id, T(21), T(23), T(5), T(7));

        rig.Days.Day(Mar1, matching, At(Mar1, 22));
        Assert.Equal(new OwningDay(Mar1, AllocationBranch.ShiftMatching), await rig.Resolve(At(Mar2, 6)));

        rig.Days.Day(Mar1, matching, At(Mar1, 18));
        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 6)));
    }

    [Fact]
    public async Task Branch5_A8_inverted_start_window_tests_the_earliest_punch()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        // Legacy anchors on slot 1 = 22:00, inside 21-23 → yesterday. The earliest is 18:00, outside.
        rig.Days.Day(Mar1, ShiftMatching(rig, ShiftMatchType.StartAndEnd, night.Id, T(21), T(23), T(5), T(7)),
            At(Mar1, 22), At(Mar1, 18));

        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 6))).Date);
    }

    [Fact]
    public async Task Branch5_needs_a_punch_yesterday_and_a_shift_matching_kind()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        var matching = ShiftMatching(rig, ShiftMatchType.EndOnly, night.Id, endFrom: T(5), endTo: T(7));

        rig.Days.Day(Mar1, matching); // no punches yesterday
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 6))).Date);

        matching.Kind = TemplateKind.FixedSchedule1Range; // rules present, wrong kind
        rig.Db.SaveChanges();
        rig.Days.Day(Mar1, matching, At(Mar1, 22));
        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 6))).Date);
    }

    [Fact]
    public async Task Branch5_a_rule_whose_target_template_does_not_exist_never_matches()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, ShiftMatching(rig, ShiftMatchType.EndOnly, Guid.NewGuid(), endFrom: T(5), endTo: T(7)), At(Mar1, 22));

        Assert.Equal(Mar2, (await rig.Resolve(At(Mar2, 6))).Date);
    }

    // ================= The vault's eleven, verbatim (swipe on 2 March 2023) =================

    private static Rig Vault(TimeOnly? nightShiftEnd = null, (bool On, TimeOnly? After)? offset = null,
        TimeSpan? window = null, bool swipesYesterday = false, bool swipesToday = false)
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => { t.NightShiftEndTime = nightShiftEnd; t.AllocateToPreviousDayWindow = window; }),
            swipesYesterday ? [At(Mar1, 18)] : []);
        if (offset is { } o)
            rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = o.On; t.OffsetAfterTime = o.After; }));
        if (swipesToday)
            rig.Days.Day(Mar2, null, At(Mar2, 1));
        return rig;
    }

    [Fact] public async Task V01() => Assert.Equal(Mar1, (await Vault(nightShiftEnd: T(4)).Resolve(At(Mar2, 2))).Date);
    [Fact] public async Task V02() => Assert.Equal(Mar2, (await Vault(nightShiftEnd: T(4)).Resolve(At(Mar2, 4, 30))).Date);
    [Fact] public async Task V03() => Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await Vault().Resolve(At(Mar2, 4, 30)));
    [Fact] public async Task V04() => Assert.Equal(Mar3, (await Vault(offset: (true, T(1))).Resolve(At(Mar2, 2))).Date);
    [Fact] public async Task V05() => Assert.Equal(Mar2, (await Vault(offset: (true, T(5))).Resolve(At(Mar2, 4, 30))).Date);
    [Fact] public async Task V06()
    {
        Assert.Equal(Mar2, (await Vault(offset: (false, T(1))).Resolve(At(Mar2, 4, 30))).Date);
        Assert.Equal(Mar2, (await Vault(offset: (true, null)).Resolve(At(Mar2, 4, 30))).Date);
    }
    [Fact] public async Task V07() => Assert.Equal(Mar1, (await Vault(window: TimeSpan.FromHours(9), swipesYesterday: true).Resolve(At(Mar2, 2))).Date);
    [Fact] public async Task V08() => Assert.Equal(Mar2, (await Vault(window: TimeSpan.FromHours(9), swipesYesterday: true).Resolve(At(Mar2, 4))).Date);
    [Fact] public async Task V09() => Assert.Equal(Mar2, (await Vault(window: TimeSpan.FromHours(9), swipesYesterday: true, swipesToday: true).Resolve(At(Mar2, 3))).Date);
    [Fact] public async Task V10() => Assert.Equal(Mar2, (await Vault(window: TimeSpan.FromHours(9)).Resolve(At(Mar2, 2))).Date);
    [Fact] public async Task V11() => Assert.Equal(Mar2, (await Vault(swipesYesterday: true).Resolve(At(Mar2, 3))).Date);

    // ================= Order and fallback =================

    [Fact]
    public async Task Order_branch1_wins_over_branch2()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.NightShiftEndTime = T(4)));
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(1); }));

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.NightShiftEnd), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task Order_branch2_wins_over_branch3()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9)), At(Mar1, 18));
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(1); }));

        Assert.Equal(new OwningDay(Mar3, AllocationBranch.OffsetToNextDay), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task Order_branch3_wins_over_branch5()
    {
        var rig = new Rig();
        var night = rig.Template(t => t.NightShiftEndTime = T(8));
        var matching = ShiftMatching(rig, ShiftMatchType.EndOnly, night.Id, endFrom: T(1), endTo: T(7));
        matching.AllocateToPreviousDayWindow = TimeSpan.FromHours(9);
        rig.Db.SaveChanges();
        rig.Days.Day(Mar1, matching, At(Mar1, 18));

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.AllocateToPreviousDay), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task A9_when_every_branch_misses_the_punch_keeps_its_own_date()
    {
        var rig = new Rig();
        rig.Days.Day(Mar1, rig.Template(t => { t.NightShiftEndTime = T(4); t.AllocateToPreviousDayWindow = TimeSpan.FromHours(9); }), At(Mar1, 8));
        rig.Days.Day(Mar3, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(22); }));

        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 12)));
    }

    [Fact]
    public async Task A9_with_no_days_at_all_the_punch_keeps_its_own_date()
    {
        var rig = new Rig();
        Assert.Equal(new OwningDay(Mar2, AllocationBranch.OwnDate), await rig.Resolve(At(Mar2, 2)));
    }

    [Fact]
    public async Task The_master_active_on_the_punch_date_supplies_the_neighbours_fields()
    {
        var rig = new Rig();
        var master = rig.Template(t => t.NightShiftEndTime = T(7));
        var day = rig.Template(t => { t.NightShiftEndTime = T(2); t.OverriddenByMasterTemplate = true; });
        // A3 inverted (P1): a one-day assignment on the punch's own date applies.
        rig.Db.MasterTemplateAssignments.Add(new MasterTemplateAssignment
            { EmployeeId = EmployeeId, MasterTemplateId = master.Id, StartDate = Mar2, EndDate = Mar2 });
        rig.Db.SaveChanges();
        rig.Days.Day(Mar1, day);

        Assert.Equal(new OwningDay(Mar1, AllocationBranch.NightShiftEnd), await rig.Resolve(At(Mar2, 5)));
    }

    // ================= Local wall clock, midnight and DST =================

    [Fact]
    public async Task Times_are_the_home_zones_wall_clock_not_utc()
    {
        var rig = new Rig();
        var mar10 = new DateOnly(2026, 3, 10);
        var mar11 = new DateOnly(2026, 3, 11);
        rig.Days.Day(mar10, rig.Template(t => t.NightShiftEndTime = T(4)));

        // 02:30 in Tashkent is 21:30 UTC the day before; its local date is the 11th, before 04:00 → the 10th.
        var punch = At(mar11, 2, 30, zone: Tashkent);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 21, 30, 0, TimeSpan.Zero), punch);
        Assert.Equal(new OwningDay(mar10, AllocationBranch.NightShiftEnd), await rig.Resolve(punch, Tashkent));

        // Read as UTC the same instant is 21:30 on the 10th — not before 04:00, and its own day is the 10th.
        Assert.Equal(new OwningDay(mar10, AllocationBranch.OwnDate), await rig.Resolve(punch, ZoneId.Utc));
    }

    [Fact]
    public async Task A_night_shift_crossing_local_midnight_lands_whole_on_the_day_it_started()
    {
        var rig = new Rig();
        var fri = new DateOnly(2026, 3, 13);
        var sat = new DateOnly(2026, 3, 14);
        rig.Days.Day(fri, rig.Template(t => t.NightShiftEndTime = T(6)));

        Assert.Equal(fri, (await rig.Resolve(At(fri, 22, zone: Ljubljana), Ljubljana)).Date);
        Assert.Equal(fri, (await rig.Resolve(At(sat, 0, 0, zone: Ljubljana), Ljubljana)).Date);
        Assert.Equal(fri, (await rig.Resolve(At(sat, 5, 59, zone: Ljubljana), Ljubljana)).Date);
        Assert.Equal(sat, (await rig.Resolve(At(sat, 6, zone: Ljubljana), Ljubljana)).Date);
    }

    [Fact]
    public async Task Dst_spring_forward_night_shift_end_is_a_wall_clock_time()
    {
        // Ljubljana 2026-03-29: 02:00 CET → 03:00 CEST.
        var rig = new Rig();
        var sat = new DateOnly(2026, 3, 28);
        var sun = new DateOnly(2026, 3, 29);
        rig.Days.Day(sat, rig.Template(t => t.NightShiftEndTime = T(3, 30)));

        var justBefore = new DateTimeOffset(2026, 3, 29, 1, 15, 0, TimeSpan.Zero); // 03:15 CEST
        var atEnd = new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero);      // 03:30 CEST
        Assert.Equal(sat, (await rig.Resolve(justBefore, Ljubljana)).Date);
        Assert.Equal(new OwningDay(sun, AllocationBranch.OwnDate), await rig.Resolve(atEnd, Ljubljana));
    }

    [Fact]
    public async Task Dst_fall_back_both_occurrences_of_an_ambiguous_time_compare_by_wall_clock()
    {
        // Ljubljana 2026-10-25: 03:00 CEST → 02:00 CET; 02:15 happens twice.
        var rig = new Rig();
        var sat = new DateOnly(2026, 10, 24);
        var sun = new DateOnly(2026, 10, 25);
        rig.Days.Day(sat, rig.Template(t => t.NightShiftEndTime = T(2, 30)));

        Assert.Equal(sat, (await rig.Resolve(new DateTimeOffset(2026, 10, 25, 0, 15, 0, TimeSpan.Zero), Ljubljana)).Date); // 02:15 CEST
        Assert.Equal(sat, (await rig.Resolve(new DateTimeOffset(2026, 10, 25, 1, 15, 0, TimeSpan.Zero), Ljubljana)).Date); // 02:15 CET
        Assert.Equal(sun, (await rig.Resolve(new DateTimeOffset(2026, 10, 25, 1, 45, 0, TimeSpan.Zero), Ljubljana)).Date); // 02:45 CET
    }

    [Fact]
    public async Task Dst_spring_forward_branch3_window_is_elapsed_time_inverting_legacy_wall_clock_arithmetic()
    {
        var rig = new Rig();
        var sat = new DateOnly(2026, 3, 28);
        var sun = new DateOnly(2026, 3, 29);
        var first = At(sat, 22, zone: Ljubljana); // 21:00 UTC
        rig.Days.Day(sat, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(8)), first);

        // 06:30 CEST = 04:30 UTC: 7h30 elapsed, inside 8h → Saturday. Legacy: 22:00 + 8h = 06:00 wall < 06:30 → Sunday.
        var punch = At(sun, 6, 30, zone: Ljubljana);
        Assert.Equal(TimeSpan.FromHours(7.5), punch - first);
        Assert.Equal(new OwningDay(sat, AllocationBranch.AllocateToPreviousDay), await rig.Resolve(punch, Ljubljana));
        // Boundary: exactly 8h elapsed (07:00 CEST) stays Sunday.
        Assert.Equal(sun, (await rig.Resolve(first + TimeSpan.FromHours(8), Ljubljana)).Date);
    }

    [Fact]
    public async Task Dst_fall_back_branch3_window_is_elapsed_time_inverting_legacy_wall_clock_arithmetic()
    {
        var rig = new Rig();
        var sat = new DateOnly(2026, 10, 24);
        var sun = new DateOnly(2026, 10, 25);
        var first = At(sat, 22, zone: Ljubljana); // 20:00 UTC (CEST)
        rig.Days.Day(sat, rig.Template(t => t.AllocateToPreviousDayWindow = TimeSpan.FromHours(8)), first);

        // 05:30 CET = 04:30 UTC: 8h30 elapsed → Sunday. Legacy: 22:00 + 8h = 06:00 wall > 05:30 → Saturday.
        var punch = At(sun, 5, 30, zone: Ljubljana);
        Assert.Equal(TimeSpan.FromHours(8.5), punch - first);
        Assert.Equal(new OwningDay(sun, AllocationBranch.OwnDate), await rig.Resolve(punch, Ljubljana));
    }

    // ================= At record time: PunchService, the freeze, and A10 =================

    private static readonly Guid SiteId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000102");

    private static (PunchService Service, TimeAttendanceDbContext Db) Recorder(Rig rig) =>
        (new PunchService(rig.Db,
            new StubDirectory(new EmployeeSummary(EmployeeId, "E0102", "Night Worker", null, SiteId, null, new DateOnly(2020, 1, 1), null, false)),
            new NoopEventStream(), Options.Create(new PunchDeduplicationOptions()), Options.Create(new PunchTimingOptions()),
            new FixedZones(Ljubljana), rig.Service, rig.Days, new SystemClock(new FixedTime(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)))),
         rig.Db);

    [Fact]
    public async Task At_record_time_the_allocated_day_is_frozen_on_the_punch_and_the_instant_is_kept()
    {
        var rig = new Rig();
        var fri = new DateOnly(2026, 3, 13);
        var sat = new DateOnly(2026, 3, 14);
        rig.Days.Day(fri, rig.Template(t => t.NightShiftEndTime = T(6)));
        var (service, db) = Recorder(rig);

        var at = At(sat, 2, zone: Ljubljana);
        var result = await service.RecordAsync(new RecordPunchRequest("E0102", PunchDirection.Out, PunchSource.Web, at.ToString("O")), null, default);

        Assert.Null(result.Error);
        Assert.Equal(fri, result.Punch!.LocalDate);
        Assert.Equal(at, result.Punch.Timestamp); // legacy re-composed it onto Friday (73.V5.19.0.0.sql:259-260); WM does not
        var day = Assert.Single(await service.GetTimesheetAsync(EmployeeId, fri, sat, default));
        Assert.Equal(fri, day.Date);
        Assert.Empty(rig.Days.Created); // Friday existed
    }

    [Fact]
    public async Task A10_inverted_a_punch_whose_day_has_no_clocking_creates_the_day_and_is_never_lost()
    {
        var rig = new Rig();
        var sat = new DateOnly(2026, 3, 14);
        var (service, db) = Recorder(rig);

        // Legacy: ProcessQueryGetClockingForSwipe returns NULL → 'Clock Record not found', swipe discarded.
        var result = await service.RecordAsync(
            new RecordPunchRequest("E0102", PunchDirection.In, PunchSource.Web, At(sat, 9, zone: Ljubljana).ToString("O")), null, default);

        Assert.Null(result.Error);
        Assert.Equal([sat], rig.Days.Created);
        Assert.Equal(1, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A10_the_day_created_is_the_allocated_day_not_the_calendar_day()
    {
        var rig = new Rig();
        var sat = new DateOnly(2026, 3, 14);
        var sun = new DateOnly(2026, 3, 15);
        // Saturday was never generated; Sunday exists, armed from 22:00. Saturday 23:00 → Sunday, so nothing is created.
        rig.Days.Day(sun, rig.Template(t => { t.OffsetTransactionToNextDay = true; t.OffsetAfterTime = T(22); }));
        var (service, _) = Recorder(rig);

        var result = await service.RecordAsync(
            new RecordPunchRequest("E0102", PunchDirection.In, PunchSource.Web, At(sat, 23, zone: Ljubljana).ToString("O")), null, default);

        Assert.Equal(sun, result.Punch!.LocalDate);
        Assert.Empty(rig.Days.Created);
    }
}

/// <summary>
/// The eleven Given/When/Thens of <c>Documentation\Swipe to clocking allocation.md:43-118</c>, lifted
/// for plan 002. They run above against a Clocking store double; these placeholders are the list 002 P3
/// re-runs against the real Clocking aggregate and calendar job. Numbering is the vault's order.
/// </summary>
public sealed class VaultAllocationExamplesFor002
{
    private const string Why = "Plan 002 P3: re-run against the real Clocking store (010 P2 covers it against FakeClockingDays).";

    [Fact(Skip = Why)] public void V01_swipe_2Mar_0200_yesterday_night_shift_end_0400_goes_to_1Mar() { }
    [Fact(Skip = Why)] public void V02_swipe_2Mar_0430_yesterday_night_shift_end_0400_stays_2Mar() { }
    [Fact(Skip = Why)] public void V03_swipe_2Mar_0430_yesterday_night_shift_end_empty_falls_through() { }
    [Fact(Skip = Why)] public void V04_swipe_2Mar_0200_tomorrow_offset_on_after_0100_goes_to_3Mar() { }
    [Fact(Skip = Why)] public void V05_swipe_2Mar_0430_tomorrow_offset_on_after_0500_stays_2Mar() { }
    [Fact(Skip = Why)] public void V06_swipe_2Mar_0430_offset_off_or_time_empty_falls_through() { }
    [Fact(Skip = Why)] public void V07_swipe_2Mar_0200_none_today_yesterday_first_1800_window_0900_goes_to_1Mar() { }
    [Fact(Skip = Why)] public void V08_swipe_2Mar_0400_none_today_yesterday_first_1800_window_0900_stays_2Mar() { }
    [Fact(Skip = Why)] public void V09_swipe_2Mar_0300_today_has_swipes_stays_2Mar() { }
    [Fact(Skip = Why)] public void V10_swipe_2Mar_0200_no_swipes_either_day_stays_2Mar() { }
    [Fact(Skip = Why)] public void V11_swipe_2Mar_0300_window_empty_stays_2Mar() { }
}
