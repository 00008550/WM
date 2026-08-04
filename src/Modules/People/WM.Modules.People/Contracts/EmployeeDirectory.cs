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

    /// <summary>Active employees the caller may see. Drives the live attendance board.</summary>
    Task<IReadOnlyList<EmployeeSummary>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Every active employee, ignoring data scope. For system/background work only
    /// (seeding, scheduled jobs) where there is no signed-in user. Deliberately
    /// verbose so misuse in a request path is obvious in review.
    /// </summary>
    Task<IReadOnlyList<EmployeeSummary>> ListAllActiveUnscopedAsync(CancellationToken ct = default);
}

/// <summary>
/// <paramref name="DepartmentId"/> is here because department is a data-scope axis: a consumer
/// deciding who may see this employee needs both axes, not just the site. Null means "no
/// department", which never satisfies a department constraint.
/// </summary>
public sealed record EmployeeSummary(
    Guid Id, string Code, string FullName, string? JobTitle, Guid SiteId, Guid? DepartmentId = null);
