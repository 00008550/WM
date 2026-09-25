using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// Which rule placed a punch on its day (plan 010 P2). Numbered as legacy
/// <c>dbo.ProcessQueryGetClockingForSwipe</c> orders them (<c>37.V3.6.1.0.sql:748-868</c>) and as
/// P1 numbered them: the master-template override legacy interleaves is not a date-moving branch — it
/// is folded into <see cref="Contracts.EffectiveDayTemplate"/> before any branch runs.
/// </summary>
public enum AllocationBranch
{
    /// <summary>Branch 1: before yesterday's <c>NightShiftEndTime</c> → yesterday (<c>:774-785</c>).</summary>
    NightShiftEnd = 1,

    /// <summary>Branch 2: after tomorrow's <c>OffsetAfterTime</c>, when armed → tomorrow (<c>:787-799</c>).</summary>
    OffsetToNextDay = 2,

    /// <summary>Branch 3: nothing today yet, and within yesterday's window from its first punch →
    /// yesterday (<c>:801-834</c>).</summary>
    AllocateToPreviousDay = 3,

    /// <summary>Branch 5: yesterday is a shift-matching day and this punch completes one of its rules
    /// → yesterday (<c>:836-864</c>).</summary>
    ShiftMatching = 5,

    /// <summary>No branch fired: the punch's own local date (<c>:866-867</c>).</summary>
    OwnDate = 0,
}

/// <summary>The resolved working day, and the rule that chose it.</summary>
public sealed record OwningDay(DateOnly Date, AllocationBranch Branch);

/// <summary>
/// <i>"Which working day does this punch belong to?"</i> — the one named seam every punch passes
/// through when it is recorded (plan 008 P4). The answer is <b>frozen on the punch</b>
/// (<see cref="Domain.Punch.LocalDate"/>, user decision 2026-09-24, open question 4) and read back
/// from there; nothing re-derives it at read time, so a later zone edit, tzdata update <b>or template
/// edit</b> never moves a punch to another day. Moving one is an explicit, audited recalculate.
///
/// <para>
/// The production body is <see cref="DayAllocationService"/> (plan 010 P2): legacy's allocation
/// branches, evaluated on the wall clock of <paramref name="zone"/> against the neighbouring days'
/// effective templates.
/// </para>
/// </summary>
public interface IOwningDayResolver
{
    Task<OwningDay> ResolveAsync(Guid employeeId, DateTimeOffset instant, ZoneId zone, CancellationToken ct);
}

/// <summary>
/// The naive answer — the calendar date on the wall clock of the zone, no template consulted. 008 P4's
/// body, kept for callers that must not allocate (tests of the zone seam alone). Not registered.
/// </summary>
public sealed class LocalCalendarDayResolver : IOwningDayResolver
{
    public Task<OwningDay> ResolveAsync(Guid employeeId, DateTimeOffset instant, ZoneId zone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return Task.FromResult(new OwningDay(zone.DateAt(instant), AllocationBranch.OwnDate));
    }
}
