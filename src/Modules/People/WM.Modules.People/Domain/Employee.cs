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
/// How much notice the leaver worked — a maintained lookup, exactly like <see cref="LeavingReason"/>.
/// Legacy's is <c>dbo.LeaveNoticePeriods</c> (<c>Id, Name</c> plus the usual flag), offered on the
/// leaver tab beside the reason (<c>_Leaver.cshtml:17-20</c>).
///
/// <para>
/// <b>Schema only in 007 P1</b> (user decision, 2026-08-15): the column and the table ride this
/// migration because adding them later costs a second migration against a table this one has already
/// rewritten. Nothing writes it yet — there is no upsert field, no validation and no screen — and it
/// inherits <see cref="LeavingReason"/>'s gap exactly: <b>no maintenance surface</b>, so until one is
/// planned the table can only be filled by SQL.
/// </para>
/// </summary>
public sealed class LeaveNoticePeriod : Entity
{
    public required string Name { get; set; }

    /// <summary>False retires the entry: still readable on the records that used it, no longer offered.</summary>
    public bool IsActive { get; set; } = true;
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
///
/// <para>
/// <b>Employment and suspension are two facts, and this class keeps them apart.</b>
/// <see cref="IsEmployedOn"/> answers the <i>window</i> question only; suspension is a separate
/// boolean the caller composes with when its question needs it, and <see cref="StatusOn"/> is where
/// the two are combined for display. Legacy never fuses them either:
/// <c>ActiveNotFired()</c> is literally <c>Active().NotFired()</c> — two <c>Where</c> clauses a
/// caller opts into (<c>Logic/Extensions/EmployeeExtensions.cs:22-27</c>) — <c>NotFired()</c> ships
/// alone in production paths that never mention <c>IsActive</c>
/// (<c>ServiceTasks.cs:214</c>, <c>PersonnelService.cs:1841</c>,
/// <c>GlobalNotificationsService.cs:557, 596</c>, <c>EposDinerService.cs:279</c>,
/// <c>ExpenseMileagesController.cs:414</c>), <c>dbo.IsActiveEmployment</c> takes a date and cannot
/// see <c>IsActive</c> at all, and <c>TipsService.cs:75-79</c> gates the two on independent
/// caller-supplied flags. Fusing them would silently drop suspended people out of every
/// "who was employed on D?" question — a replay, an accrual pro-rate, a payroll re-run — which is
/// the opposite of what those callers are asking.
/// </para>
/// </summary>
public static class Employment
{
    /// <summary>
    /// The employment <b>window</b>, evaluated the way legacy evaluates it: a date, always against a
    /// reference date (<c>dbo.IsActiveEmployment(@dischargeDate, @referenceDate)</c>,
    /// <c>76.V5.22.0.0.sql:25-38</c>, which likewise takes only dates).
    /// <para>
    /// <b>Suspension is deliberately not a parameter.</b> A caller asking "may this person work
    /// today?" wants <c>!IsSuspended &amp;&amp; IsEmployedOn(today)</c> and must write both halves;
    /// a caller asking "was this person on the payroll in March?" wants this one alone. See the
    /// class remarks for why legacy is the authority on that split.
    /// </para>
    /// </summary>
    /// <param name="employedFrom">First day of employment, inclusive.</param>
    /// <param name="employedUntil"><b>Last day of employment, inclusive.</b> Null = open-ended.</param>
    /// <param name="on">The date the question is asked about — never implicitly today.</param>
    public static bool IsEmployedOn(DateOnly employedFrom, DateOnly? employedUntil, DateOnly on) =>
        employedFrom <= on
        && (employedUntil is null || on <= employedUntil);

    /// <summary>
    /// The label for <paramref name="on"/> — the one place the two facts <i>are</i> combined, because
    /// a display badge has to say one word. Suspension is checked first because it overrides the
    /// window: legacy's view says an inactive employee with a future discharge date is active — the
    /// <c>AND … OR …</c> precedence defect at <c>78.V5.24.0.0.sql:74-80</c> — and WM says the
    /// opposite. <see cref="EmployeeStatus.Active"/> is therefore exactly
    /// <c>!isSuspended &amp;&amp; IsEmployedOn(…)</c>, the composition every "may they work?" caller
    /// wants.
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
    /// <b>Contractual end of employment / end of paid notice</b> — legacy
    /// <c>Employees.FinalEmploymentDate</c>, and a <i>different</i> fact from
    /// <see cref="EmployedUntil"/>'s last day at work. Legacy's entitlement engine ends the
    /// employment window at <c>FinalEmploymentDate ?? DischargeDate</c>, the final date taking
    /// precedence (<c>EmployeeAccrualCalculationsService.cs:728-730</c>, <c>:941</c>;
    /// <c>AccrualsCalculationRepository.cs:53-57</c>).
    /// <para>
    /// <b>Stored, not yet read</b> (user decision, 2026-08-15 — "schema now, behaviour later").
    /// <see cref="IsEmployedOn"/> still ends the window at <see cref="EmployedUntil"/> alone, exactly
    /// as this portion shipped. Making the precedence real changes accrual and payroll answers, so it
    /// needs its own portion with its own tests — do not quietly add <c>?? </c> here.
    /// </para>
    /// </summary>
    public DateOnly? FinalEmploymentDate { get; set; }

    /// <summary>
    /// The date the person handed in their notice — legacy's leaver tab field
    /// (<c>_Leaver.cshtml:42-45</c>). Neither end of the employment window: it is the HR paperwork
    /// date, and it is what a notice-period figure is measured from.
    /// <para><b>Stored, not yet read</b> — see <see cref="FinalEmploymentDate"/>.</para>
    /// </summary>
    public DateOnly? ResignationDate { get; set; }

    /// <summary>
    /// How much notice was given, from the maintained lookup — legacy
    /// <c>dbo.LeaveNoticePeriods</c> (<c>_Leaver.cshtml:17-20</c>).
    /// <para><b>Stored, not yet read</b> — see <see cref="FinalEmploymentDate"/>. In particular it is
    /// <i>not</i> cleared with <see cref="EmployedUntil"/> yet, because nothing can set it.</para>
    /// </summary>
    public Guid? LeaveNoticePeriodId { get; set; }

    /// <summary>
    /// Administratively suspended — legacy <c>IsActive</c>, inverted so that <c>false</c> is the
    /// good default. Deliberately <b>not</b> the same fact as leaving, and deliberately <b>not</b>
    /// part of <see cref="IsEmployedOn"/>: legacy's <c>SetEmployeesLeaver:122-145</c> writes the
    /// discharge date and pointedly does not touch <c>IsActive</c>, its accrual calculation
    /// pro-rates on the dates while never reading <c>IsActive</c> at all, and every legacy caller
    /// that wants both composes them itself (<c>Active().NotFired()</c>). Callers here do the same.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>Why they left, from the maintained lookup. Null unless <see cref="EmployedUntil"/> is set.</summary>
    public Guid? LeavingReasonId { get; set; }

    /// <summary>
    /// Free text beside the reason — legacy <c>Employees.AdditionalLeaverComments</c>,
    /// <c>nvarchar(500)</c> (<c>:32539</c>). Null unless <see cref="EmployedUntil"/> is set.
    /// <para>
    /// <b>HR's note about the person, not the person's own record.</b> It is excluded from
    /// <c>GET /api/me/employee</c> for that reason (user decision, 2026-08-15); it is still visible
    /// to anyone holding <c>employees.view</c>, which field-group rights (plan 004) will narrow.
    /// </para>
    /// </summary>
    public string? LeaverComments { get; set; }

    public string FullName => $"{FirstName} {LastName}";

    /// <inheritdoc cref="Employment.IsEmployedOn"/>
    public bool IsEmployedOn(DateOnly on) => Employment.IsEmployedOn(EmployedFrom, EmployedUntil, on);

    /// <inheritdoc cref="Employment.StatusOn"/>
    public EmployeeStatus StatusOn(DateOnly on) => Employment.StatusOn(IsSuspended, EmployedFrom, EmployedUntil, on);

    /// <summary>
    /// <see cref="IsEmployedOn"/> as a query filter, because the object form cannot reach the
    /// database. It is written out rather than derived from the shared rule so that what EF
    /// translates is a plain comparison chain every provider handles — the two are held together by
    /// <c>EmploymentPredicateTests.The_query_filter_and_the_object_predicate_answer_the_same_question</c>,
    /// which walks a matrix of windows and reference dates through both. Legacy's six copies drifted
    /// because nothing compared them.
    /// <para>
    /// Like the object form, this is the <b>window only</b>. A query that wants people who may
    /// actually work adds <c>.Where(e =&gt; !e.IsSuspended)</c> and says so.
    /// </para>
    /// </summary>
    public static Expression<Func<Employee, bool>> EmployedOn(DateOnly on) =>
        e => e.EmployedFrom <= on
             && (e.EmployedUntil == null || on <= e.EmployedUntil);
}
