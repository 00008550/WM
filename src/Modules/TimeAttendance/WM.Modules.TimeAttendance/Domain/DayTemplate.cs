using WM.SharedKernel.Domain;

namespace WM.Modules.TimeAttendance.Domain;

/// <summary>
/// Legacy <c>DailyModels.ModelType</c> (<c>SharedLogic\Enums\ShiftType.cs:4-17</c>), values kept so
/// an import is a cast. Only the discriminator lands in plan 010 — the shape each kind implies is
/// Phase 2. Allocation tests exactly one value: <see cref="ShiftMatching"/>
/// (<c>37.V3.6.1.0.sql:834</c>).
/// </summary>
public enum TemplateKind
{
    FixedSchedule1Range = 1,
    FixedSchedule2Ranges = 2,
    VariableSchedule2Ranges = 3,
    VariableSchedule4Ranges = 4,
    FixedSchedule1RangeWithBreaks = 5,
    VariableSchedule2RangesWithBreaks = 6,
    ShiftMatching = 7,
    SplitShifts = 8,
    MultiModel = 9,
    FlexiModel = 10,
    FixedScheduleWith6PairsOfSwipes = 11,
}

/// <summary>
/// A daily template — the allocation subset only (plan 010 P1). Legacy <c>dbo.DailyModels</c> has 124
/// columns; swipe→day allocation (<c>dbo.ProcessQueryGetClockingForSwipe</c>,
/// <c>37.V3.6.1.0.sql:748-868</c>) reads five of them plus the master-override flag, and nothing else
/// here is needed until Phase 2.
///
/// <para>
/// <b>Ownership is provisional.</b> These fields belong to the Rules module
/// (<c>ARCHITECTURE.md</c>), which does not exist yet. TimeAttendance holds them until Rules takes
/// ownership in Phase 2, after which TimeAttendance reads them through
/// <see cref="Contracts.IDayTemplateDirectory"/> — the contract is the seam that makes that move
/// cheap. Plan 010, Target design / Open question 1.
/// </para>
/// </summary>
public sealed class DayTemplate : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }

    public TemplateKind Kind { get; set; }

    /// <summary>Legacy <c>NightShiftEndTime</c>. A punch strictly before it belongs to yesterday (branch 1).</summary>
    public TimeOnly? NightShiftEndTime { get; set; }

    /// <summary>Legacy <c>NightShiftStartTime</c>. A punch strictly after it belongs to tomorrow — only
    /// when <see cref="OffsetTransactionToNextDay"/> is set (branch 2, <c>:795</c>).</summary>
    public TimeOnly? OffsetAfterTime { get; set; }

    /// <summary>Legacy <c>ShiftToSunday</c>, renamed: it has never meant Sunday. It is the switch that
    /// arms <see cref="OffsetAfterTime"/>.</summary>
    public bool OffsetTransactionToNextDay { get; set; }

    /// <summary>Legacy <c>InterswipeIntervalToMoveToYesterday</c> — stored there as a <c>time</c>, it is a
    /// duration measured from yesterday's first punch (branch 3, <c>:806-829</c>).</summary>
    public TimeSpan? AllocateToPreviousDayWindow { get; set; }

    /// <summary>
    /// Legacy <c>TreatAsMasterDailyModel</c>, <b>inverted</b> so the name says what it does:
    /// <c>true</c> ⇔ <c>TreatAsMasterDailyModel = 0</c> ⇔ an employee's active master template, if any,
    /// supplies this day's allocation fields (<c>:775-778</c>). Defaults to <c>true</c> because the
    /// legacy column defaults to <c>0</c> as a CLR bool. An import maps <c>!TreatAsMasterDailyModel</c>.
    /// </summary>
    public bool OverriddenByMasterTemplate { get; set; } = true;

    /// <summary>Legacy <c>dbo.DailyModelShiftMatchingRules</c>. Read only when <see cref="Kind"/> is
    /// <see cref="TemplateKind.ShiftMatching"/>.</summary>
    public List<ShiftMatchingRule> ShiftMatchingRules { get; set; } = [];
}
