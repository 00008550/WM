using WM.SharedKernel.Domain;

namespace WM.Modules.TimeAttendance.Domain;

/// <summary>Legacy <c>DailyModelShiftMatchingRules.MatchType</c> (<c>37.V3.6.1.0.sql:857-858</c>).</summary>
public enum ShiftMatchType
{
    /// <summary>The punch falls in the end window.</summary>
    EndOnly = 1,
    /// <summary>Yesterday's first punch falls in the start window <b>and</b> the punch in the end window.</summary>
    StartAndEnd = 2,
}

/// <summary>
/// A child row of a <see cref="TemplateKind.ShiftMatching"/> template: "a day whose punches look like
/// this is really governed by <see cref="TemplateToAssignId"/>". Legacy
/// <c>dbo.DailyModelShiftMatchingRules</c>, all eight columns. Evaluated by plan 010 P2 (edge case A7);
/// P1 only persists and exposes it.
/// </summary>
public sealed class ShiftMatchingRule : Entity
{
    public Guid DayTemplateId { get; set; }
    public ShiftMatchType MatchType { get; set; }
    public TimeOnly? StartTimeFrom { get; set; }
    public TimeOnly? StartTimeTo { get; set; }
    public TimeOnly? EndTimeFrom { get; set; }
    public TimeOnly? EndTimeTo { get; set; }
    public Guid TemplateToAssignId { get; set; }
}
