using WM.Modules.People.Domain;

namespace WM.Modules.People.Contracts;

/// <summary>
/// Public contract other modules may depend on (instead of People internals).
/// </summary>
public interface IEmployeeDirectory
{
    /// <summary>Scoped to the caller — returns null for an employee they may not see.</summary>
    Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Scoped to the caller — returns null for an employee they may not see.</summary>
    Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The caller's <b>own</b> record, ignoring data scope. Scope governs what a caller may see of
    /// <i>other</i> people; a site-scoped manager filed at a site outside their scope is still
    /// themselves (008 P4 review). Pass only the employee id from the caller's token — never an id
    /// taken from the request.
    /// </summary>
    Task<EmployeeSummary?> FindSelfAsync(Guid ownEmployeeId, CancellationToken ct = default);

    /// <summary>
    /// Employees the caller may see who were employed on <paramref name="on"/>. Replaces
    /// <c>ListActiveAsync</c>: employment is a window, so the question needs a date. Callers that
    /// mean "now" pass <see cref="DateOnly.FromDateTime"/> of today explicitly rather than letting
    /// the directory guess — legacy's planning board guessed, and answered next quarter's roster
    /// against today's date (<c>PlanningService.cs:32-57</c>).
    /// <para>
    /// <b>The window only — suspended employees are included.</b> Suspension is a separate fact and
    /// travels on every <see cref="EmployeeSummary"/>, so a caller whose question is "who may work?"
    /// filters on <see cref="EmployeeSummary.IsSuspended"/> and one whose question is "who was on the
    /// payroll that day?" — a replay, an accrual pro-rate — does not. Folding it in here would
    /// silently answer the second question with the first one's answer.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnAsync(DateOnly on, CancellationToken ct = default);

    /// <summary>
    /// Every employee employed on <paramref name="on"/>, ignoring data scope. For system/background
    /// work only (seeding, scheduled jobs) where there is no signed-in user. Deliberately verbose so
    /// misuse in a request path is obvious in review.
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnUnscopedAsync(DateOnly on, CancellationToken ct = default);

    /// <summary>
    /// Employees the caller may see who are employed on <b>their own</b> local today — the calendar
    /// date on the wall clock of the zone their home site resolves to (<see cref="ISiteTimeZones"/>),
    /// not one date for everybody (plan 008 P4). At 12:00 UTC it is already tomorrow in Auckland and
    /// still today in Honolulu; a single <see cref="ListEmployedOnAsync"/> date is wrong for one of
    /// them. Resolved in one batch: one employee read, one site-tree read.
    /// <para>The window only — suspended employees are included, exactly as
    /// <see cref="ListEmployedOnAsync"/>.</para>
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedAtLocalTodayAsync(CancellationToken ct = default);

    /// <summary>
    /// <see cref="ListEmployedAtLocalTodayAsync"/> ignoring data scope. System/background work only
    /// (seeding), where there is no signed-in user.
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedAtLocalTodayUnscopedAsync(CancellationToken ct = default);
}

/// <summary>
/// <paramref name="DepartmentId"/> is here because department is a data-scope axis: a consumer
/// deciding who may see this employee needs both axes, not just the site. Null means "no
/// department", which never satisfies a department constraint.
///
/// <para>
/// The employment window travels with the summary — and with <see cref="IsEmployedOn"/> beside it —
/// so a consumer can decide employment <b>as at the date it cares about</b> without reaching into
/// People's tables (invariant 1). TimeAttendance's punch boundary needs exactly that: the decision
/// belongs to the punch's own timestamp, not to <c>UtcNow</c> (plan 007 P2, edge case 8).
/// </para>
///
/// <para>
/// <paramref name="IsSuspended"/> is a <b>separate</b> raw fact, not folded into
/// <see cref="IsEmployedOn"/>. A consumer deciding whether someone may punch composes
/// <c>!IsSuspended &amp;&amp; IsEmployedOn(timestamp)</c> — or reads
/// <see cref="StatusOn"/> <c>== Active</c>, which is the same thing — while a consumer replaying who
/// was on the payroll uses the window alone.
/// </para>
///
/// <para>
/// Nothing here is optional, including <paramref name="DepartmentId"/>, which used to default to
/// null. A defaulted employment window would default to <i>employed since 0001-01-01, forever</i> —
/// a fail-open, in the one record whose purpose is to fail closed.
/// </para>
/// </summary>
public sealed record EmployeeSummary(
    Guid Id,
    string Code,
    string FullName,
    string? JobTitle,
    Guid SiteId,
    Guid? DepartmentId,
    DateOnly EmployedFrom,
    DateOnly? EmployedUntil,
    bool IsSuspended)
{
    /// <inheritdoc cref="Employment.IsEmployedOn"/>
    public bool IsEmployedOn(DateOnly on) => Employment.IsEmployedOn(EmployedFrom, EmployedUntil, on);

    /// <inheritdoc cref="Employment.StatusOn"/>
    public EmployeeStatus StatusOn(DateOnly on) => Employment.StatusOn(IsSuspended, EmployedFrom, EmployedUntil, on);
}
