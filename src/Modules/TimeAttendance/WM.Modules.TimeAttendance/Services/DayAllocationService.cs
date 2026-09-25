using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Domain;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// Swipe → day allocation (plan 010 P2): a port of legacy <c>dbo.ProcessQueryGetClockingForSwipe</c>,
/// whose latest definition is the <c>ALTER</c> at <c>Database\Versioning\37.V3.6.1.0.sql:748-868</c>
/// (no later script redefines it; <c>73.V5.19.0.0.sql:232</c> only calls it). The branches run in
/// legacy's order and the first that fires wins; if none does, the punch's own local date.
///
/// <list type="number">
/// <item><b>Night-shift end</b> (<c>:774-785</c>) — local time strictly before yesterday's
///   <c>NightShiftEndTime</c> → yesterday.</item>
/// <item><b>Offset to next day</b> (<c>:787-799</c>) — tomorrow's template is armed
///   (<c>ShiftToSunday</c>) and local time is strictly after its <c>NightShiftStartTime</c> → tomorrow.</item>
/// <item><b>Allocate to previous day</b> (<c>:801-834</c>) — no punch on today yet, and the punch falls
///   strictly within <c>InterswipeIntervalToMoveToYesterday</c> of yesterday's first punch → yesterday.</item>
/// <item>(4, the master override, is applied inside <see cref="EffectiveDayTemplate"/>: one master,
///   chosen for the punch's own date, supplies every neighbour's fields — <c>:765-769</c>.)</item>
/// <item><b>Shift matching</b> (<c>:836-864</c>) — yesterday's own template is
///   <see cref="TemplateKind.ShiftMatching"/>, one of its rules matches (end window; or start window on
///   yesterday's first punch <b>and</b> end window), and that rule's target template's own
///   <c>NightShiftEndTime</c> is strictly after the punch's local time → yesterday.</item>
/// </list>
///
/// <para>
/// <b>Wall-clock times are compared on the wall clock of the employee's home-site zone</b> — legacy
/// compared a zoneless <c>time</c> to a zoneless <c>time</c> (<c>:759-760</c>); a template's "04:00"
/// means 04:00 where the employee works. A neighbouring day with no Clocking, no template, or a
/// template id that no longer exists offers nothing, as legacy's inner joins did.
/// </para>
///
/// <para><b>Deliberate inversions vs legacy</b> (recorded in plan 010, P2):</para>
/// <list type="bullet">
/// <item><b>A8</b> — "yesterday's first punch" is the <b>earliest</b> punch, not the first non-null
///   badge slot (<c>:823-825</c>, <c>:845-847</c>). Applies to branches 3 and 5.</item>
/// <item><b>Branch 3 measures elapsed time</b> — first punch instant + window, against the punch
///   instant. Legacy added the window to a wall-clock <c>datetime</c> (<c>:830</c>), so across a DST
///   change its window was an hour longer or shorter than configured.</item>
/// <item><b>The instant survives allocation.</b> Legacy re-composed the swipe as its time-of-day on the
///   allocated date (<c>73.V5.19.0.0.sql:259-260</c>) — a 02:00 swipe moved to yesterday became a
///   <c>datetime</c> 24 h earlier than it happened. WM keeps <see cref="Punch.Timestamp"/> and moves
///   only <see cref="Punch.LocalDate"/>.</item>
/// <item><b>A10</b> — a resolved day with no Clocking is created, not a discarded swipe: see
///   <see cref="IClockingDays.EnsureAsync"/>, called by <see cref="PunchService"/>.</item>
/// <item><b>A3</b> — the master window is tested against the punch's own date, not yesterday and
///   tomorrow (P1, <see cref="MasterTemplateAssignment"/>).</item>
/// </list>
/// </summary>
public sealed class DayAllocationService(IClockingDays days, IDayTemplateDirectory templates) : IOwningDayResolver
{
    public async Task<OwningDay> ResolveAsync(Guid employeeId, DateTimeOffset instant, ZoneId zone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var wallClock = TimeZoneInfo.ConvertTime(instant, zone.Info).DateTime;
        var date = DateOnly.FromDateTime(wallClock);
        var time = TimeOnly.FromDateTime(wallClock);
        var yesterday = date.AddDays(-1);
        var tomorrow = date.AddDays(1);

        var yesterdayDay = await days.FindAsync(employeeId, yesterday, ct);
        var yesterdayTemplate = await EffectiveAsync(employeeId, date, yesterdayDay, ct);

        // Branch 1 — `<` is strict (:784): a punch at exactly NightShiftEndTime stays today (A4).
        if (yesterdayTemplate?.NightShiftEndTime is { } nightShiftEnd && time < nightShiftEnd)
            return new(yesterday, AllocationBranch.NightShiftEnd);

        // Branch 2 — `>` is strict (:798): a punch at exactly the offset time stays today (A5). The time
        // alone does nothing without the switch (:795, A6) — EffectiveDayTemplate.OffsetsToNextDay.
        var tomorrowTemplate = await EffectiveAsync(employeeId, date, await days.FindAsync(employeeId, tomorrow, ct), ct);
        if (tomorrowTemplate is { OffsetsToNextDay: true, OffsetAfterTime: { } offsetAfter } && time > offsetAfter)
            return new(tomorrow, AllocationBranch.OffsetToNextDay);

        // A8, inverted: yesterday's first punch is the earliest one, whatever order the store holds them in.
        DateTimeOffset? firstOfYesterday = yesterdayDay is { Punches.Count: > 0 } ? yesterdayDay.Punches.Min() : null;

        // Branch 3 — only when nothing is on today yet (:803-809). `>` is strict (:830): a punch at
        // exactly first + window stays today. Elapsed time, not wall-clock arithmetic (see summary).
        var today = await days.FindAsync(employeeId, date, ct);
        if (today is not { Punches.Count: > 0 }
            && yesterdayTemplate?.AllocateToPreviousDayWindow is { } window
            && firstOfYesterday is { } first
            && first + window > instant)
            return new(yesterday, AllocationBranch.AllocateToPreviousDay);

        // Branch 5 — yesterday's OWN kind and rules, never a master's (:839; P1's EffectiveDayTemplate).
        if (yesterdayTemplate is { Kind: TemplateKind.ShiftMatching } && firstOfYesterday is { } anchor)
        {
            var firstTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(anchor, zone.Info).DateTime);
            var targets = yesterdayTemplate.ShiftMatchingRules
                .Where(rule => Matches(rule, firstTime, time))
                .Select(rule => rule.TemplateToAssignId)
                .Distinct()
                .ToList();
            if (targets.Count > 0)
            {
                var ends = await templates.NightShiftEndTimesAsync(targets, ct);
                if (ends.Values.Any(end => end is { } e && e > time))
                    return new(yesterday, AllocationBranch.ShiftMatching);
            }
        }

        return new(date, AllocationBranch.OwnDate);
    }

    /// <summary>
    /// <c>:857-858</c>. Every bound is inclusive, and a missing bound never matches (SQL's <c>NULL</c>
    /// comparison). <see cref="ShiftMatchType.EndOnly"/> still needs yesterday to have a punch
    /// (<c>@firstSwipeOfYesterday is not null</c>) — the caller only gets here if it does.
    /// </summary>
    private static bool Matches(ShiftMatchingRuleView rule, TimeOnly firstOfYesterday, TimeOnly time)
    {
        var endMatches = rule.EndTimeFrom <= time && rule.EndTimeTo >= time;
        return rule.MatchType switch
        {
            ShiftMatchType.EndOnly => endMatches,
            ShiftMatchType.StartAndEnd => endMatches
                && rule.StartTimeFrom <= firstOfYesterday && rule.StartTimeTo >= firstOfYesterday,
            _ => false,
        };
    }

    /// <summary>The neighbour's effective template. The master is chosen for the punch's own
    /// <paramref name="punchDate"/> — legacy picks one master per swipe (<c>:765-769</c>).</summary>
    private async Task<EffectiveDayTemplate?> EffectiveAsync(
        Guid employeeId, DateOnly punchDate, ClockingDay? day, CancellationToken ct) =>
        day?.DayTemplateId is { } templateId
            ? await templates.ResolveAsync(employeeId, punchDate, templateId, ct)
            : null;
}
