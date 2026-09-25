using WM.Modules.TimeAttendance.Domain;

namespace WM.Modules.TimeAttendance.Contracts;

/// <summary>
/// Resolves the template that <b>effectively</b> governs an employee's day for swipe→day allocation
/// (plan 010 P1; consumed by P2 and plan 002 P3). The only door to <see cref="DayTemplate"/> data —
/// when the Rules module takes ownership in Phase 2 this interface stays and its implementation moves.
/// </summary>
public interface IDayTemplateDirectory
{
    /// <summary>
    /// The effective allocation template for <paramref name="employeeId"/> on <paramref name="date"/>.
    ///
    /// <para>
    /// <paramref name="dayTemplateId"/> is the day's own template. It is an argument, not a lookup,
    /// because it lives on the day (legacy <c>Clockings.DailyModelID</c>) and the day — the Clocking
    /// aggregate — is plan 002's. The master assignment is looked up here by (employee, date).
    /// </para>
    /// Returns <c>null</c> when <paramref name="dayTemplateId"/> does not exist: a dangling template
    /// is a data defect for the caller to surface, never a silent empty template (plan 010, Invert of
    /// <c>DailyBrowserDetailsViewModel.cs:61</c>).
    /// </summary>
    Task<EffectiveDayTemplate?> ResolveAsync(Guid employeeId, DateOnly date, Guid dayTemplateId, CancellationToken ct);

    /// <summary>
    /// The templates' <b>own</b> <c>NightShiftEndTime</c>, no master override — what shift matching
    /// tests a matched rule's target against (<c>37.V3.6.1.0.sql:851-860</c> reads
    /// <c>DailyModels.NightShiftEndTime</c> of <c>ModelToAssignId</c> directly; plan 010 P2, A7).
    /// An id that does not exist is absent from the result, so it can never match.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TimeOnly?>> NightShiftEndTimesAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken ct);
}

public sealed record ShiftMatchingRuleView(
    ShiftMatchType MatchType,
    TimeOnly? StartTimeFrom,
    TimeOnly? StartTimeTo,
    TimeOnly? EndTimeFrom,
    TimeOnly? EndTimeTo,
    Guid TemplateToAssignId);

/// <summary>
/// The five allocation fields as they apply to one employee-day, after the master override.
/// </summary>
/// <param name="DayTemplateId">The day's own template.</param>
/// <param name="AppliedMasterTemplateId">The master whose fields were used, or <c>null</c> if the day's own were.</param>
/// <param name="Kind">Always the day's own — legacy branch 5 reads the day's <c>ModelType</c>, never the master's (<c>:832-834</c>).</param>
/// <param name="ShiftMatchingRules">Always the day's own, for the same reason (<c>:852-853</c>).</param>
public sealed record EffectiveDayTemplate(
    Guid DayTemplateId,
    Guid? AppliedMasterTemplateId,
    TemplateKind Kind,
    TimeOnly? NightShiftEndTime,
    TimeOnly? OffsetAfterTime,
    bool OffsetTransactionToNextDay,
    TimeSpan? AllocateToPreviousDayWindow,
    IReadOnlyList<ShiftMatchingRuleView> ShiftMatchingRules)
{
    /// <summary>
    /// Applies the master override, field for field as <c>37.V3.6.1.0.sql:775-815</c> does.
    /// The master wins when one is active <b>and</b> the day's template is
    /// <see cref="DayTemplate.OverriddenByMasterTemplate"/> (edge cases A1/A2).
    ///
    /// <para>
    /// One legacy asymmetry is <b>kept</b>, not fixed: branch 2's switch is
    /// <c>(overridden AND master.ShiftToSunday = 1) OR day.ShiftToSunday = 1</c> (<c>:795</c>) — the day's
    /// own switch still arms the master's <c>NightShiftStartTime</c>. Mirrored so P2's allocation
    /// matches legacy on migrated data; not in the plan's Invert list.
    /// </para>
    /// </summary>
    public static EffectiveDayTemplate Resolve(DayTemplate day, DayTemplate? activeMaster)
    {
        var rules = day.ShiftMatchingRules
            .Select(r => new ShiftMatchingRuleView(r.MatchType, r.StartTimeFrom, r.StartTimeTo, r.EndTimeFrom, r.EndTimeTo, r.TemplateToAssignId))
            .ToList();

        var master = day.OverriddenByMasterTemplate ? activeMaster : null;
        if (master is null)
            return new(day.Id, null, day.Kind, day.NightShiftEndTime, day.OffsetAfterTime,
                day.OffsetTransactionToNextDay, day.AllocateToPreviousDayWindow, rules);

        return new(day.Id, master.Id, day.Kind, master.NightShiftEndTime, master.OffsetAfterTime,
            master.OffsetTransactionToNextDay || day.OffsetTransactionToNextDay,
            master.AllocateToPreviousDayWindow, rules);
    }

    /// <summary>Legacy branch 2's guard (<c>:795</c>, <c>:798</c>): a time alone does nothing (edge case A6).</summary>
    public bool OffsetsToNextDay => OffsetTransactionToNextDay && OffsetAfterTime is not null;
}
