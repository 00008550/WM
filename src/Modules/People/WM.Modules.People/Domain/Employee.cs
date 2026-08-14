using System.Linq.Expressions;
using WM.SharedKernel.Domain;

namespace WM.Modules.People.Domain;

public sealed class Site : Entity
{
    public required string Name { get; set; }
    public Guid? ParentId { get; set; }
    public string TimeZone { get; set; } = "UTC";

    public List<Site> Children { get; set; } = [];
}

public sealed class Department : Entity
{
    public required string Name { get; set; }
    public Guid SiteId { get; set; }
}

/// <summary>
/// Why someone's employment ended — customer vocabulary, not WM's, so it is a maintained lookup
/// rather than an enum. Legacy's is <c>dbo.LeaveReasons</c> (<c>Id, Name, IsActive</c>,
/// <c>HorioDB.designer.cs:53138-53148</c>), maintained under <c>Menu_Personnel_LeaveReasons</c>;
/// resignation, redundancy, TUPE and dismissal are one customer's list and not another's.
///
/// <para>
/// <b>Retired, never deleted.</b> <see cref="IsActive"/> is the whole point of the entity: an
/// employee who left five years ago keeps the reason they left for, while the reason stops being
/// offered for new leavers. A delete would either orphan those records or rewrite history.
/// </para>
///
/// <para>
/// <b>Named <c>LeavingReason</c>, and legacy's table stays <c>dbo.LeaveReasons</c>.</b> In HR
/// English "leave" is an <i>absence</i> — annual leave, sick leave — and legacy's absence
/// vocabulary is a different, 35-column, rule-carrying table (<c>dbo.Absence</c>,
/// <c>:8743</c>). Naming this one <c>LeaveReason</c> beside a future <c>AbsenceType</c> would
/// re-create exactly that conflation (user decision, 2026-08-06 — plan 007 decision 4).
/// </para>
/// </summary>
public sealed class LeavingReason : Entity
{
    public required string Name { get; set; }

    /// <summary>False retires the reason: still readable on the records that used it, no longer offered.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A display label derived from the employment window at a reference date, and <b>never stored</b>.
/// Legacy's own person vocabulary is three computed values — active / leaver / inactive, from
/// <c>IsActive × dbo.IsActiveEmployment</c> (<c>PersonnelService.cs:1890-1904</c>) — and its one
/// stored-status equivalent, <c>dbo.ActiveEmployeesView</c>, is where its precedence defect lives.
/// A stored status is a second answer that can disagree with the dates; this one cannot.
/// </summary>
/// <remarks>
/// The numbering deliberately keeps <c>1</c> and <c>2</c> on the nearest neighbours of the enum this
/// replaces (<c>OnLeave</c>, <c>Terminated</c>), so a browser holding a stale bundle mislabels
/// nothing badly. <c>OnLeave</c> itself is gone: a temporary absence is a dated fact owned by the
/// Absence module, not a lifecycle state on the person (measured, `TLW-PEOPLE-MODEL.md` §4.1a).
/// </remarks>
public enum EmployeeStatus
{
    Active = 0,
    Suspended = 1,
    Leaver = 2,
    NotYetStarted = 3,
}

/// <summary>
/// WM's single employment predicate. Legacy re-derives this in <b>six</b> places — the SQL function,
/// the view, two service filters, the extension methods and the planning board — and two of the six
/// disagree (<c>TLW-PEOPLE-MODEL.md</c> §4.1a, §8). Everything in WM that asks "was this person
/// employed?" comes through here.
/// </summary>
public static class Employment
{
    /// <summary>
    /// Employment as legacy models it, and evaluated the way legacy evaluates it: two facts, one of
    /// them a date, always against a reference date
    /// (<c>dbo.IsActiveEmployment(@dischargeDate, @referenceDate)</c>,
    /// <c>76.V5.22.0.0.sql:25-38</c>).
    /// </summary>
    /// <param name="isSuspended">Administrative suspension (legacy <c>IsActive</c>, inverted). Wins outright.</param>
    /// <param name="employedFrom">First day of employment, inclusive.</param>
    /// <param name="employedUntil"><b>Last day of employment, inclusive.</b> Null = open-ended.</param>
    /// <param name="on">The date the question is asked about — never implicitly today.</param>
    public static bool IsEmployedOn(bool isSuspended, DateOnly employedFrom, DateOnly? employedUntil, DateOnly on) =>
        !isSuspended
        && employedFrom <= on
        && (employedUntil is null || on <= employedUntil);

    /// <summary>
    /// The label for <paramref name="on"/>. Suspension is checked first because it overrides the
    /// window: legacy's view says an inactive employee with a future discharge date is active — the
    /// <c>AND … OR …</c> precedence defect at <c>78.V5.24.0.0.sql:74-80</c> — and WM says the
    /// opposite.
    /// </summary>
    public static EmployeeStatus StatusOn(bool isSuspended, DateOnly employedFrom, DateOnly? employedUntil, DateOnly on) =>
        isSuspended ? EmployeeStatus.Suspended
        : employedUntil is { } until && until < on ? EmployeeStatus.Leaver
        : employedFrom > on ? EmployeeStatus.NotYetStarted
        : EmployeeStatus.Active;
}

public sealed class Employee : AuditableEntity
{
    /// <summary>Badge/payroll number — unique per installation.</summary>
    public required string Code { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }

    /// <summary>First day of employment, inclusive. Legacy <c>EnterDate</c>; was WM's <c>HireDate</c>.</summary>
    public DateOnly EmployedFrom { get; set; }

    /// <summary>
    /// <b>Last day of employment, inclusive</b> — null means open-ended. Legacy
    /// <c>Employees.DischargeDate</c> (<c>HorioDB.designer.cs:29831</c>).
    /// <para>
    /// Legacy's employment window actually ends at <c>FinalEmploymentDate ?? DischargeDate</c> — a
    /// <i>second</i> leaving date with <b>higher precedence</b>
    /// (<c>EmployeeAccrualCalculationsService.cs:728-741</c>,
    /// <c>AccrualsCalculationRepository.cs:53-57</c>). WM adopts only <c>DischargeDate</c>. That is
    /// deliberate and sufficient for phase 1; phase 3 will meet the second date, which has no owner
    /// yet (<c>TLW-PEOPLE-MODEL.md</c> §3.1's 33 unowned columns, §4.1a point 2).
    /// </para>
    /// </summary>
    public DateOnly? EmployedUntil { get; set; }

    /// <summary>
    /// Administratively suspended — legacy <c>IsActive</c>, inverted so that <c>false</c> is the
    /// good default. Deliberately <b>not</b> the same fact as leaving: legacy's
    /// <c>SetEmployeesLeaver:122-145</c> writes the discharge date and pointedly does not touch
    /// <c>IsActive</c>, and its accrual calculation pro-rates on the dates while never reading
    /// <c>IsActive</c> at all.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>Why they left, from the maintained lookup. Null unless <see cref="EmployedUntil"/> is set.</summary>
    public Guid? LeavingReasonId { get; set; }

    /// <summary>
    /// Free text beside the reason — legacy <c>Employees.AdditionalLeaverComments</c>,
    /// <c>nvarchar(500)</c> (<c>:32539</c>). Null unless <see cref="EmployedUntil"/> is set.
    /// </summary>
    public string? LeaverComments { get; set; }

    public string FullName => $"{FirstName} {LastName}";

    /// <inheritdoc cref="Employment.IsEmployedOn"/>
    public bool IsEmployedOn(DateOnly on) => Employment.IsEmployedOn(IsSuspended, EmployedFrom, EmployedUntil, on);

    /// <inheritdoc cref="Employment.StatusOn"/>
    public EmployeeStatus StatusOn(DateOnly on) => Employment.StatusOn(IsSuspended, EmployedFrom, EmployedUntil, on);

    /// <summary>
    /// <see cref="IsEmployedOn"/> as a query filter, because the object form cannot reach the
    /// database. It is written out rather than derived from the shared rule so that what EF
    /// translates is a plain comparison chain every provider handles — the two are held together by
    /// <c>EmploymentPredicateTests.The_query_filter_and_the_object_predicate_answer_the_same_question</c>,
    /// which walks a matrix of windows and reference dates through both. Legacy's six copies drifted
    /// because nothing compared them.
    /// </summary>
    public static Expression<Func<Employee, bool>> EmployedOn(DateOnly on) =>
        e => !e.IsSuspended
             && e.EmployedFrom <= on
             && (e.EmployedUntil == null || on <= e.EmployedUntil);
}
