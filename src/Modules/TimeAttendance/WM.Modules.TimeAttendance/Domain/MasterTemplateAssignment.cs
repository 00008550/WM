using WM.SharedKernel.Domain;

namespace WM.Modules.TimeAttendance.Domain;

/// <summary>
/// A dated assignment of a master template to an employee — legacy
/// <c>dbo.EmployeeMasterDailyModels</c> (5 columns). <see cref="EmployeeId"/> is People's id and
/// deliberately carries no foreign key: another module's table is not ours to constrain (invariant 1).
/// </summary>
public sealed class MasterTemplateAssignment : Entity
{
    public Guid EmployeeId { get; set; }
    public Guid MasterTemplateId { get; set; }

    /// <summary>Inclusive; <c>null</c> = open-started.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Inclusive; <c>null</c> = open-ended.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Whether the assignment covers <paramref name="date"/>, both bounds inclusive.
    ///
    /// <para>
    /// <b>Inverted from legacy (plan 010 edge case A3).</b> Legacy tests
    /// <c>StartDate &lt;= yesterday AND EndDate &gt;= tomorrow</c> (<c>37.V3.6.1.0.sql:768-769</c>), so an
    /// assignment must cover the day on <i>both</i> sides to apply: a one-day assignment never applies,
    /// and neither do the first and last day of any assignment. WM tests the date itself. A customer
    /// migrating assignments will see them take effect one day earlier and end one day later than
    /// legacy allocation treated them.
    /// </para>
    /// </summary>
    public bool IsActiveOn(DateOnly date) =>
        (StartDate is null || StartDate <= date) && (EndDate is null || EndDate >= date);

    /// <summary>
    /// The assignment governing <paramref name="date"/>. Legacy takes <c>top 1</c> with no
    /// <c>order by</c> (<c>:765</c>), so overlapping assignments resolve arbitrarily there; WM makes it
    /// deterministic — the most recently started wins (open-started counts as earliest), then the
    /// latest-created (ids are v7, time-ordered).
    /// </summary>
    public static MasterTemplateAssignment? SelectFor(IEnumerable<MasterTemplateAssignment> assignments, DateOnly date) =>
        assignments
            .Where(a => a.IsActiveOn(date))
            .OrderByDescending(a => a.StartDate ?? DateOnly.MinValue)
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();
}
