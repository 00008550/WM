namespace WM.SharedKernel.Security;

/// <summary>
/// How wide a user's view of employee data is.
/// Successor to TLW's <c>RoleBasedEmployeeFilterService</c> + <c>SiteItemPermission</c>,
/// but expressed as data rather than scattered authorizer classes.
/// </summary>
public enum DataScopeKind
{
    /// <summary>No employee data at all. The safe default for a user with no groups.</summary>
    None = 0,
    /// <summary>Only the employee this user is linked to (self-service).</summary>
    Self = 1,
    /// <summary>Employees in the listed departments.</summary>
    Departments = 2,
    /// <summary>Employees at the listed sites (optionally including child sites).</summary>
    Sites = 3,
    /// <summary>Every employee. Administrators only.</summary>
    All = 4,
}

/// <summary>
/// A user's effective view, resolved from every security group they belong to.
/// Scopes are additive — the widest grant wins, which matches how people expect
/// group membership to behave ("I was added to a group, so I see more").
/// </summary>
public sealed record EffectiveDataScope(
    DataScopeKind Kind,
    IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> DepartmentIds,
    Guid? SelfEmployeeId)
{
    public static readonly EffectiveDataScope None =
        new(DataScopeKind.None, new HashSet<Guid>(), new HashSet<Guid>(), null);

    public static EffectiveDataScope All() =>
        new(DataScopeKind.All, new HashSet<Guid>(), new HashSet<Guid>(), null);

    public bool SeesEverything => Kind == DataScopeKind.All;
    public bool SeesNothing => Kind == DataScopeKind.None;

    /// <summary>
    /// Whether a specific employee is visible. Kept here (rather than only as a
    /// query filter) so the diagnostics endpoint can explain a decision without
    /// duplicating the rule.
    /// </summary>
    public bool CanSee(Guid employeeId, Guid siteId, Guid? departmentId) => Kind switch
    {
        DataScopeKind.All => true,
        DataScopeKind.Sites => SiteIds.Contains(siteId),
        DataScopeKind.Departments => departmentId is { } d && DepartmentIds.Contains(d),
        DataScopeKind.Self => SelfEmployeeId == employeeId,
        _ => false,
    };
}

/// <summary>
/// Resolves the caller's effective scope. Implemented in the Identity module and
/// consumed by every module that reads employee-shaped data.
/// </summary>
public interface IDataScopeResolver
{
    /// <summary>Effective scope for the signed-in user, resolved per request.</summary>
    Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default);

    /// <summary>Effective scope for an arbitrary user — used by access diagnostics.</summary>
    Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default);
}
