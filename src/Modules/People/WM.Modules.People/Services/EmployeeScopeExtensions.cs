using WM.Modules.People.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.People.Services;

/// <summary>
/// Applies a user's effective data scope to an employee query.
///
/// Every employee-reading query must go through this. Scoping at the query means a
/// new endpoint is scoped by construction rather than by the author remembering —
/// which is how legacy ended up needing a diagnostics subsystem to find the gaps.
/// </summary>
public static class EmployeeScopeExtensions
{
    public static IQueryable<Employee> WithinScope(this IQueryable<Employee> query, EffectiveDataScope scope) =>
        scope.Kind switch
        {
            DataScopeKind.All => query,
            DataScopeKind.Sites => query.Where(e => scope.SiteIds.Contains(e.SiteId)),
            DataScopeKind.Departments => query.Where(e =>
                e.DepartmentId != null && scope.DepartmentIds.Contains(e.DepartmentId.Value)),
            DataScopeKind.Self => query.Where(e => e.Id == scope.SelfEmployeeId),
            // Unknown or None: return nothing. Failing closed is the only safe default.
            _ => query.Where(_ => false),
        };
}
