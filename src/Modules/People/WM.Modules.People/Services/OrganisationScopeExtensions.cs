using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.People.Services;

/// <summary>
/// The site and department lists a caller is offered, cut by the same scope that cuts the employee
/// list (003 P3). Before this, <c>GET /api/sites</c> returned every site in the estate to anyone
/// holding <c>employees.view</c> — the organisation chart leaked even where the people did not.
///
/// <para>
/// The rule is "the places the caller's scope reaches", arm by arm with
/// <see cref="EmployeeScopeExtensions.WithinScope"/>:
/// </para>
/// <list type="bullet">
/// <item><b>Departments</b> scope reaches its departments, and the sites those departments sit at —
/// without the site, a department-scoped manager could not fill the site field of the employee
/// editor, and 007 P4 refuses a department at any site but its own.</item>
/// <item><b>Sites</b> scope reaches its sites and every department at them.</item>
/// <item><b>Self</b> reaches the site and department of the caller's own record, nothing more.</item>
/// <item><b>None</b>, or anything unrecognised, reaches nothing. Fail closed.</item>
/// </list>
/// </summary>
public static class OrganisationScopeExtensions
{
    public static IQueryable<Site> SitesWithinScope(this PeopleDbContext db, EffectiveDataScope scope) =>
        scope.Kind switch
        {
            DataScopeKind.All => db.Sites,
            DataScopeKind.Sites => db.Sites.Where(s => scope.SiteIds.Contains(s.Id)),
            DataScopeKind.Departments => db.Sites.Where(s =>
                db.Departments.Any(d => d.SiteId == s.Id && scope.DepartmentIds.Contains(d.Id))),
            DataScopeKind.Self => db.Sites.Where(s =>
                db.Employees.Any(e => e.Id == scope.SelfEmployeeId && e.SiteId == s.Id)),
            _ => db.Sites.Where(_ => false),
        };

    public static IQueryable<Department> DepartmentsWithinScope(this PeopleDbContext db, EffectiveDataScope scope) =>
        scope.Kind switch
        {
            DataScopeKind.All => db.Departments,
            DataScopeKind.Sites => db.Departments.Where(d => scope.SiteIds.Contains(d.SiteId)),
            DataScopeKind.Departments => db.Departments.Where(d => scope.DepartmentIds.Contains(d.Id)),
            DataScopeKind.Self => db.Departments.Where(d =>
                db.Employees.Any(e => e.Id == scope.SelfEmployeeId && e.DepartmentId == d.Id)),
            _ => db.Departments.Where(_ => false),
        };
}
