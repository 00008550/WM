namespace WM.Modules.People.Contracts;

/// <summary>
/// Public contract other modules may depend on (instead of People internals).
/// </summary>
public interface IEmployeeDirectory
{
    Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default);
    Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeSummary>> ListActiveAsync(CancellationToken ct = default);
}

public sealed record EmployeeSummary(Guid Id, string Code, string FullName, string? JobTitle, Guid SiteId);
