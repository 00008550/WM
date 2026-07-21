namespace WM.SharedKernel.Security;

/// <summary>
/// A user's resolved visibility, combined from every group they belong to.
///
/// Groups are additive and combine as a union: an employee is visible if *any*
/// group grants them. That matches how group membership is expected to behave —
/// joining a group can only widen access — and it is why narrowing is done by
/// removing membership rather than by deny rules, which are far harder to reason
/// about when several groups overlap.
/// </summary>
public sealed record EffectiveDataScope(
    bool SeesAllEmployees,
    IReadOnlySet<Guid> DepartmentIds,
    IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> EmployeeIds,
    Guid? SelfEmployeeId)
{
    public static readonly EffectiveDataScope None =
        new(false, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), null);

    /// <summary>True when the user can see no employee at all.</summary>
    public bool SeesNothing =>
        !SeesAllEmployees && DepartmentIds.Count == 0 && SiteIds.Count == 0
        && EmployeeIds.Count == 0 && SelfEmployeeId is null;

    /// <summary>
    /// Whether one employee is visible. Kept alongside the query filter so the
    /// diagnostics endpoint can explain a decision without restating the rule.
    /// </summary>
    public bool CanSee(Guid employeeId, Guid siteId, Guid? departmentId) =>
        SeesAllEmployees
        || EmployeeIds.Contains(employeeId)
        || SelfEmployeeId == employeeId
        || SiteIds.Contains(siteId)
        || (departmentId is { } d && DepartmentIds.Contains(d));
}

/// <summary>
/// A user's resolved screen permissions, combined from every group they belong to.
/// Where groups disagree the most permissive wins, consistent with the union rule
/// for data scope.
/// </summary>
public sealed class EffectiveScreenAccess(IReadOnlyDictionary<string, ScreenAccess> screens)
{
    public static readonly EffectiveScreenAccess None =
        new(new Dictionary<string, ScreenAccess>());

    public IReadOnlyDictionary<string, ScreenAccess> Screens { get; } = screens;

    public ScreenAccess For(string screenId) =>
        Screens.TryGetValue(screenId, out var access) ? access : ScreenAccess.None;

    public bool CanRead(string screenId) => For(screenId) >= ScreenAccess.Read;
    public bool CanEdit(string screenId) => For(screenId) >= ScreenAccess.Edit;
}

/// <summary>
/// Resolves what the caller may see and do. Implemented by Identity and consumed
/// by every module that reads employee-shaped data.
/// </summary>
public interface IDataScopeResolver
{
    Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default);
    Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default);
    Task<EffectiveScreenAccess> GetScreenAccessForUserAsync(Guid userId, CancellationToken ct = default);
}
