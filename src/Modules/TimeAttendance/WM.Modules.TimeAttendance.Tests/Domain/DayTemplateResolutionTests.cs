using Microsoft.EntityFrameworkCore;
using Xunit;
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;

namespace WM.Modules.TimeAttendance.Tests.Domain;

/// <summary>
/// Plan 010 P1 — the effective allocation template for (employee, date): edge cases A1, A2, A3, A6,
/// and resolution with no, an active, and an expired master assignment. Legacy reference is
/// <c>dbo.ProcessQueryGetClockingForSwipe</c>, <c>37.V3.6.1.0.sql:748-868</c>.
/// </summary>
public sealed class DayTemplateResolutionTests
{
    private static readonly Guid Employee = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
    private static readonly DateOnly SwipeDate = new(2026, 3, 11);

    private static DayTemplate Day(bool overridden = true) => new()
    {
        Code = "DAY", Name = "Day shift", Kind = TemplateKind.FixedSchedule1Range,
        NightShiftEndTime = new TimeOnly(2, 0),
        OffsetAfterTime = new TimeOnly(23, 0),
        OffsetTransactionToNextDay = false,
        AllocateToPreviousDayWindow = TimeSpan.FromHours(10),
        OverriddenByMasterTemplate = overridden,
    };

    private static DayTemplate Master() => new()
    {
        Code = "NIGHT", Name = "Night master", Kind = TemplateKind.FixedSchedule1Range,
        NightShiftEndTime = new TimeOnly(7, 0),
        OffsetAfterTime = new TimeOnly(21, 0),
        OffsetTransactionToNextDay = true,
        AllocateToPreviousDayWindow = TimeSpan.FromHours(14),
    };

    // ---- A1 / A2: who wins ----

    [Fact]
    public void A1_master_supplies_the_allocation_fields_when_the_day_is_overridable()
    {
        var day = Day(overridden: true); // legacy TreatAsMasterDailyModel = 0
        var master = Master();

        var effective = EffectiveDayTemplate.Resolve(day, master);

        Assert.Equal(master.Id, effective.AppliedMasterTemplateId);
        Assert.Equal(new TimeOnly(7, 0), effective.NightShiftEndTime);
        Assert.Equal(new TimeOnly(21, 0), effective.OffsetAfterTime);
        Assert.Equal(TimeSpan.FromHours(14), effective.AllocateToPreviousDayWindow);
        Assert.True(effective.OffsetTransactionToNextDay);
        // Kind is never the master's (branch 5 reads the day's own ModelType).
        Assert.Equal(day.Id, effective.DayTemplateId);
        Assert.Equal(day.Kind, effective.Kind);
    }

    [Fact]
    public void A2_the_days_own_template_wins_when_it_is_not_overridable()
    {
        var day = Day(overridden: false); // legacy TreatAsMasterDailyModel = 1

        var effective = EffectiveDayTemplate.Resolve(day, Master());

        Assert.Null(effective.AppliedMasterTemplateId);
        Assert.Equal(new TimeOnly(2, 0), effective.NightShiftEndTime);
        Assert.Equal(new TimeOnly(23, 0), effective.OffsetAfterTime);
        Assert.Equal(TimeSpan.FromHours(10), effective.AllocateToPreviousDayWindow);
        Assert.False(effective.OffsetTransactionToNextDay);
    }

    [Fact]
    public void The_days_own_offset_switch_still_arms_the_masters_offset_time_as_in_legacy()
    {
        // :795 — (TreatAs = 0 AND master.ShiftToSunday = 1) OR day.ShiftToSunday = 1. Kept, not fixed.
        var day = Day(overridden: true);
        day.OffsetTransactionToNextDay = true;
        var master = Master();
        master.OffsetTransactionToNextDay = false;

        var effective = EffectiveDayTemplate.Resolve(day, master);

        Assert.True(effective.OffsetTransactionToNextDay);
        Assert.Equal(new TimeOnly(21, 0), effective.OffsetAfterTime);
    }

    [Fact]
    public void Shift_matching_rules_are_the_days_own_even_when_the_master_applies()
    {
        var target = Guid.NewGuid();
        var day = Day(overridden: true);
        day.Kind = TemplateKind.ShiftMatching;
        day.ShiftMatchingRules.Add(new ShiftMatchingRule
        {
            DayTemplateId = day.Id, MatchType = ShiftMatchType.EndOnly,
            EndTimeFrom = new TimeOnly(5, 0), EndTimeTo = new TimeOnly(7, 0), TemplateToAssignId = target,
        });

        var effective = EffectiveDayTemplate.Resolve(day, Master());

        Assert.Equal(TemplateKind.ShiftMatching, effective.Kind);
        Assert.Equal(target, Assert.Single(effective.ShiftMatchingRules).TemplateToAssignId);
    }

    // ---- A6 ----

    [Fact]
    public void A6_an_offset_time_without_the_switch_does_not_offset()
    {
        var day = Day(overridden: false);
        day.OffsetTransactionToNextDay = false;
        Assert.NotNull(day.OffsetAfterTime);

        Assert.False(EffectiveDayTemplate.Resolve(day, null).OffsetsToNextDay);

        day.OffsetTransactionToNextDay = true;
        Assert.True(EffectiveDayTemplate.Resolve(day, null).OffsetsToNextDay);
    }

    // ---- A3: the master-assignment window, legacy vs WM ----

    /// <summary>Legacy's predicate, transcribed from <c>37.V3.6.1.0.sql:768-769</c> as a test oracle.
    /// It exists only here; product code implements the inverted rule.</summary>
    private static bool LegacyAppliesOn(MasterTemplateAssignment a, DateOnly swipeDate) =>
        (a.StartDate is null || a.StartDate <= swipeDate.AddDays(-1)) &&
        (a.EndDate is null || a.EndDate >= swipeDate.AddDays(1));

    [Fact]
    public void A3_a_one_day_assignment_never_applies_in_legacy_and_applies_in_WM()
    {
        var oneDay = new MasterTemplateAssignment
        {
            EmployeeId = Employee, MasterTemplateId = Guid.NewGuid(), StartDate = SwipeDate, EndDate = SwipeDate,
        };

        Assert.False(LegacyAppliesOn(oneDay, SwipeDate));   // legacy: never
        Assert.True(oneDay.IsActiveOn(SwipeDate));          // WM: inverted
    }

    [Theory]
    [InlineData(0, 10)]   // first day of the assignment
    [InlineData(10, 0)]   // last day of the assignment
    public void A3_the_first_and_last_day_of_any_assignment_are_the_rest_of_the_divergence(int daysBefore, int daysAfter)
    {
        var a = new MasterTemplateAssignment
        {
            EmployeeId = Employee, MasterTemplateId = Guid.NewGuid(),
            StartDate = SwipeDate.AddDays(-daysBefore), EndDate = SwipeDate.AddDays(daysAfter),
        };

        Assert.False(LegacyAppliesOn(a, SwipeDate));
        Assert.True(a.IsActiveOn(SwipeDate));
    }

    [Fact]
    public void A3_strictly_inside_the_window_legacy_and_WM_agree()
    {
        var a = new MasterTemplateAssignment
        {
            EmployeeId = Employee, MasterTemplateId = Guid.NewGuid(),
            StartDate = SwipeDate.AddDays(-1), EndDate = SwipeDate.AddDays(1),
        };
        Assert.True(LegacyAppliesOn(a, SwipeDate));
        Assert.True(a.IsActiveOn(SwipeDate));
    }

    [Fact]
    public void Open_bounds_cover_every_date()
    {
        var a = new MasterTemplateAssignment { EmployeeId = Employee, MasterTemplateId = Guid.NewGuid() };
        Assert.True(a.IsActiveOn(DateOnly.MinValue));
        Assert.True(a.IsActiveOn(DateOnly.MaxValue));
    }

    [Fact]
    public void Overlapping_assignments_resolve_to_the_most_recently_started()
    {
        var older = new MasterTemplateAssignment { EmployeeId = Employee, MasterTemplateId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1) };
        var newer = new MasterTemplateAssignment { EmployeeId = Employee, MasterTemplateId = Guid.NewGuid(), StartDate = new DateOnly(2026, 3, 1) };
        var open = new MasterTemplateAssignment { EmployeeId = Employee, MasterTemplateId = Guid.NewGuid() };

        Assert.Same(newer, MasterTemplateAssignment.SelectFor([open, newer, older], SwipeDate));
    }

    // ---- the contract, end to end over the module's own context ----

    private static TimeAttendanceDbContext NewDb() => new(
        new DbContextOptionsBuilder<TimeAttendanceDbContext>()
            .UseInMemoryDatabase($"daytemplate-{Guid.NewGuid()}").Options);

    private static async Task<(DayTemplateDirectory dir, DayTemplate day, DayTemplate master, TimeAttendanceDbContext db)> Seed(
        DateOnly? start, DateOnly? end, bool assign = true)
    {
        var db = NewDb();
        var day = Day(overridden: true);
        var master = Master();
        db.DayTemplates.AddRange(day, master);
        if (assign)
            db.MasterTemplateAssignments.Add(new MasterTemplateAssignment
                { EmployeeId = Employee, MasterTemplateId = master.Id, StartDate = start, EndDate = end });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (new DayTemplateDirectory(db), day, master, db);
    }

    [Fact]
    public async Task Without_a_master_assignment_the_days_own_template_is_effective()
    {
        var (dir, day, _, _) = await Seed(null, null, assign: false);

        var effective = await dir.ResolveAsync(Employee, SwipeDate, day.Id, default);

        Assert.NotNull(effective);
        Assert.Null(effective.AppliedMasterTemplateId);
        Assert.Equal(new TimeOnly(2, 0), effective.NightShiftEndTime);
    }

    [Fact]
    public async Task With_an_active_master_assignment_the_master_is_effective()
    {
        var (dir, day, master, _) = await Seed(SwipeDate.AddDays(-30), null);

        var effective = await dir.ResolveAsync(Employee, SwipeDate, day.Id, default);

        Assert.Equal(master.Id, effective!.AppliedMasterTemplateId);
        Assert.Equal(new TimeOnly(7, 0), effective.NightShiftEndTime);
    }

    [Fact]
    public async Task An_expired_master_assignment_does_not_apply()
    {
        var (dir, day, _, _) = await Seed(SwipeDate.AddDays(-30), SwipeDate.AddDays(-1));

        var effective = await dir.ResolveAsync(Employee, SwipeDate, day.Id, default);

        Assert.Null(effective!.AppliedMasterTemplateId);
        Assert.Equal(new TimeOnly(2, 0), effective.NightShiftEndTime);
    }

    [Fact]
    public async Task Another_employees_master_assignment_does_not_apply()
    {
        var (dir, day, _, _) = await Seed(null, null);

        var effective = await dir.ResolveAsync(Guid.NewGuid(), SwipeDate, day.Id, default);

        Assert.Null(effective!.AppliedMasterTemplateId);
    }

    [Fact]
    public async Task A_dangling_day_template_resolves_to_null_not_an_empty_template()
    {
        var (dir, _, _, _) = await Seed(null, null);
        Assert.Null(await dir.ResolveAsync(Employee, SwipeDate, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Templates_persist_their_shift_matching_rules_as_child_rows()
    {
        var db = NewDb();
        var target = Master();
        var day = Day();
        day.Kind = TemplateKind.ShiftMatching;
        day.ShiftMatchingRules.Add(new ShiftMatchingRule
        {
            MatchType = ShiftMatchType.StartAndEnd, StartTimeFrom = new TimeOnly(21, 0), StartTimeTo = new TimeOnly(23, 0),
            EndTimeFrom = new TimeOnly(5, 0), EndTimeTo = new TimeOnly(7, 0), TemplateToAssignId = target.Id,
        });
        db.DayTemplates.AddRange(target, day);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.DayTemplates.Include(t => t.ShiftMatchingRules).SingleAsync(t => t.Id == day.Id);
        var rule = Assert.Single(loaded.ShiftMatchingRules);
        Assert.Equal(day.Id, rule.DayTemplateId);
        Assert.Equal(ShiftMatchType.StartAndEnd, rule.MatchType);
        Assert.Equal(new TimeOnly(21, 0), rule.StartTimeFrom);
    }
}
