using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

/// <summary>
/// Resolves a user's effective visibility and screen rights from their groups.
///
/// Groups combine as a union: any group granting something is enough. For screen
/// rights the most permissive level wins, so Edit in one group beats Read in
/// another. Both rules follow from groups being additive.
/// </summary>
public sealed class DataScopeResolver(
    IdentityDbContext db,
    ICurrentUser currentUser,
    ISiteHierarchy siteHierarchy) : IDataScopeResolver
{
    public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) =>
        currentUser.UserId is { } id
            ? GetScopeForUserAsync(id, ct)
            : Task.FromResult(EffectiveDataScope.None);

    public async Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.EmployeeId, u.IsActive })
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.IsActive)
            return EffectiveDataScope.None;

        var groups = await db.Set<UserGroup>().AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => new
            {
                g.Group.ScopeType,
                g.Group.IncludeChildSites,
                DepartmentIds = g.Group.Departments.Select(d => d.DepartmentId).ToList(),
                SiteIds = g.Group.Sites.Select(s => s.SiteId).ToList(),
                EmployeeIds = g.Group.Employees.Select(e => e.EmployeeId).ToList(),
            })
            .ToListAsync(ct);

        var departmentIds = new HashSet<Guid>();
        var siteIds = new HashSet<Guid>();
        var employeeIds = new HashSet<Guid>();
        var seesAll = false;
        var expandChildSites = false;

        foreach (var group in groups)
        {
            switch (group.ScopeType)
            {
                case EmployeeScopeType.AllEmployees:
                    seesAll = true;
                    break;
                case EmployeeScopeType.ByDepartments:
                    foreach (var id in group.DepartmentIds) departmentIds.Add(id);
                    foreach (var id in group.SiteIds) siteIds.Add(id);
                    if (group.IncludeChildSites) expandChildSites = true;
                    break;
                case EmployeeScopeType.ByEmployees:
                    foreach (var id in group.EmployeeIds) employeeIds.Add(id);
                    break;
            }
        }

        if (seesAll)
            return new EffectiveDataScope(true, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), user.EmployeeId);

        if (expandChildSites && siteIds.Count > 0)
            siteIds = await siteHierarchy.ExpandWithDescendantsAsync(siteIds, ct);

        // An employee-linked user always sees themselves, with or without groups,
        // so self-service never depends on an admin remembering to grant it.
        return new EffectiveDataScope(false, departmentIds, siteIds, employeeIds, user.EmployeeId);
    }

    public async Task<EffectiveScreenAccess> GetScreenAccessForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var isActive = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.IsActive).FirstOrDefaultAsync(ct);
        if (!isActive)
            return EffectiveScreenAccess.None;

        var permissions = await db.Set<UserGroup>().AsNoTracking()
            .Where(g => g.UserId == userId)
            .SelectMany(g => g.Group.ScreenPermissions)
            .Select(p => new { p.ScreenId, p.Access })
            .ToListAsync(ct);

        var effective = new Dictionary<string, ScreenAccess>();
        foreach (var p in permissions)
        {
            // Most permissive wins where groups overlap.
            if (!effective.TryGetValue(p.ScreenId, out var current) || p.Access > current)
                effective[p.ScreenId] = p.Access;
        }

        return new EffectiveScreenAccess(effective);
    }
}
