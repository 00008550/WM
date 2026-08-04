using WM.SharedKernel.Security;

namespace WM.Api.Realtime;

/// <summary>
/// The realtime feed's scope rule, expressed as SignalR group names.
///
/// Two halves that must stay exact mirrors of each other:
/// <list type="bullet">
/// <item><see cref="ForScope"/> — the groups a <b>connection</b> joins, from its user's
/// resolved scope.</item>
/// <item><see cref="ForSubject"/> — the groups an event about an <b>employee</b> is addressed
/// to.</item>
/// </list>
/// A connection receives an event exactly when the two sets intersect, and that must agree with
/// <see cref="EffectiveDataScope.CanSee"/> for every combination. <c>AttendanceScopeGroupTests</c>
/// pins the equivalence; if the two ever disagree the leak is silent, which is why the rule is
/// one file and not two call sites.
///
/// ARCHITECTURE.md §4 says scope is applied at the query, never per endpoint. The hub is the one
/// place that cannot obey it — there is no query — so it gets this: the same rule, moved into the
/// addressing. Fan-out stays O(groups); nothing here enumerates connections or users.
///
/// The default is deliberately empty. A scope that grants nothing yields no groups, so the
/// connection hears nothing.
/// </summary>
public static class AttendanceScopeGroups
{
    /// <summary>Everyone with unrestricted scope. The only group an administrator needs.</summary>
    public const string Everyone = "scope:all";

    public static string Site(Guid siteId) => $"scope:site:{siteId:N}";

    public static string Department(Guid departmentId) => $"scope:dept:{departmentId:N}";

    public static string Self(Guid employeeId) => $"scope:self:{employeeId:N}";

    /// <summary>
    /// The groups a connection joins. Mirrors <see cref="EffectiveDataScope.CanSee"/> arm for
    /// arm — including the fact that a site-scoped user is <i>not</i> separately granted their
    /// own record. That is today's resolver semantics, not a choice made here; changing it
    /// belongs with plan 001's scope model, not with the transport.
    /// </summary>
    public static IReadOnlyList<string> ForScope(EffectiveDataScope scope) => scope.Kind switch
    {
        DataScopeKind.All => [Everyone],
        DataScopeKind.Sites => [.. scope.SiteIds.Select(Site)],
        DataScopeKind.Departments => [.. scope.DepartmentIds.Select(Department)],
        // A Self scope with no linked employee grants nothing — and so joins nothing.
        DataScopeKind.Self => scope.SelfEmployeeId is { } employeeId ? [Self(employeeId)] : [],
        // None, or an enum value from a future version. No groups: hears nothing.
        _ => [],
    };

    /// <summary>
    /// The groups an event about this employee is addressed to — one per axis the employee sits
    /// on, plus <see cref="Everyone"/>.
    /// </summary>
    public static IReadOnlyList<string> ForSubject(Guid employeeId, Guid siteId, Guid? departmentId)
    {
        var groups = new List<string>(4) { Everyone, Site(siteId), Self(employeeId) };
        // No department is not a wildcard: an employee with no department is invisible to a
        // department-scoped user, which is what EffectiveDataScope.CanSee already says.
        if (departmentId is { } id)
            groups.Add(Department(id));
        return groups;
    }
}
