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
    /// Employees the caller may see who were employed on <paramref name="on"/>. Replaces
    /// <c>ListActiveAsync</c>: employment is a window, so the question needs a date. Callers that
    /// mean "now" pass <see cref="DateOnly.FromDateTime"/> of today explicitly rather than letting
    /// the directory guess — legacy's planning board guessed, and answered next quarter's roster
    /// against today's date (<c>PlanningService.cs:32-57</c>).
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnAsync(DateOnly on, CancellationToken ct = default);

    /// <summary>
    /// Every employee employed on <paramref name="on"/>, ignoring data scope. For system/background
    /// work only (seeding, scheduled jobs) where there is no signed-in user. Deliberately verbose so
    /// misuse in a request path is obvious in review.
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnUnscopedAsync(DateOnly on, CancellationToken ct = default);
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
    public bool IsEmployedOn(DateOnly on) => Employment.IsEmployedOn(IsSuspended, EmployedFrom, EmployedUntil, on);

    /// <inheritdoc cref="Employment.StatusOn"/>
    public EmployeeStatus StatusOn(DateOnly on) => Employment.StatusOn(IsSuspended, EmployedFrom, EmployedUntil, on);
}
