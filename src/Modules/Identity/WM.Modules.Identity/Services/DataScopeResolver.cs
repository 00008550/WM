using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

/// <summary>
/// Resolves a user's effective data scope from their security groups.
///
/// Scopes are additive and the widest wins: All &gt; Sites &gt; Departments &gt; Self &gt; None.
/// That matches how group membership is expected to behave — being added to a group
/// can only ever widen what you see, never narrow it. Narrowing is done by removing
/// group membership, which is far easier to reason about than deny-rules.
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
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.EmployeeId, u.IsActive })
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.IsActive)
            return EffectiveDataScope.None;

        var groups = await db.Set<Domain.UserSecurityGroup>()
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => new
            {
                g.SecurityGroup.ScopeKind,
                g.SecurityGroup.IncludeChildSites,
                SiteIds = g.SecurityGroup.Sites.Select(s => s.SiteId).ToList(),
                DepartmentIds = g.SecurityGroup.Departments.Select(d => d.DepartmentId).ToList(),
            })
            .ToListAsync(ct);

        // An employee-linked user always sees themselves, even with no groups —
        // otherwise self-service would depend on an admin remembering to add them.
        var widest = user.EmployeeId is not null ? DataScopeKind.Self : DataScopeKind.None;
        var siteIds = new HashSet<Guid>();
        var departmentIds = new HashSet<Guid>();
        var expandChildSites = false;

        foreach (var group in groups)
        {
            if (group.ScopeKind > widest) widest = group.ScopeKind;
            foreach (var s in group.SiteIds) siteIds.Add(s);
            foreach (var d in group.DepartmentIds) departmentIds.Add(d);
            if (group.IncludeChildSites) expandChildSites = true;
        }

        if (widest == DataScopeKind.All)
            return EffectiveDataScope.All();

        if (widest == DataScopeKind.Sites && expandChildSites && siteIds.Count > 0)
            siteIds = await siteHierarchy.ExpandWithDescendantsAsync(siteIds, ct);

        return new EffectiveDataScope(widest, siteIds, departmentIds, user.EmployeeId);
    }
}
