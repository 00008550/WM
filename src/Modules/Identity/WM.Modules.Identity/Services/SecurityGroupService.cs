using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

public sealed record SecurityGroupListItem(
    Guid Id, string Name, string Description, bool IsSystem,
    DataScopeKind ScopeKind, bool IncludeChildSites,
    Guid[] SiteIds, Guid[] DepartmentIds, int MemberCount);

public sealed record SecurityGroupUpsertRequest(
    string Name, string Description, DataScopeKind ScopeKind,
    bool IncludeChildSites, Guid[] SiteIds, Guid[] DepartmentIds);

public sealed record GroupMutationResult(bool Succeeded, string? Error, Guid? Id)
{
    public static GroupMutationResult Ok(Guid id) => new(true, null, id);
    public static GroupMutationResult Fail(string error) => new(false, error, null);
}

public sealed class SecurityGroupService(IdentityDbContext db)
{
    public async Task<IReadOnlyList<SecurityGroupListItem>> ListAsync(CancellationToken ct) =>
        await db.SecurityGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new SecurityGroupListItem(
                g.Id, g.Name, g.Description, g.IsSystem, g.ScopeKind, g.IncludeChildSites,
                g.Sites.Select(s => s.SiteId).ToArray(),
                g.Departments.Select(d => d.DepartmentId).ToArray(),
                g.Members.Count))
            .ToListAsync(ct);

    public async Task<GroupMutationResult> CreateAsync(SecurityGroupUpsertRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length < 2)
            return GroupMutationResult.Fail("Group name must be at least 2 characters.");
        if (await db.SecurityGroups.AnyAsync(g => g.Name == name, ct))
            return GroupMutationResult.Fail($"A group named '{name}' already exists.");
        if (Validate(request) is { } error)
            return GroupMutationResult.Fail(error);

        var group = new SecurityGroup
        {
            Name = name,
            Description = request.Description.Trim(),
            ScopeKind = request.ScopeKind,
            IncludeChildSites = request.IncludeChildSites,
            Sites = request.SiteIds.Distinct().Select(id => new SecurityGroupSite { SiteId = id }).ToList(),
            Departments = request.DepartmentIds.Distinct().Select(id => new SecurityGroupDepartment { DepartmentId = id }).ToList(),
        };

        // Write the composed shape alongside the legacy one so groups created after this portion
        // are not left behind by the migration. The legacy pair stays authoritative until P3.
        (group.RuleKind, group.Constraints) = LegacyScopeMapping.FromLegacy(
            request.ScopeKind, request.IncludeChildSites, request.SiteIds, request.DepartmentIds);

        db.SecurityGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(group.Id);
    }

    public async Task<GroupMutationResult> UpdateAsync(Guid id, SecurityGroupUpsertRequest request, CancellationToken ct)
    {
        var group = await db.SecurityGroups
            .Include(g => g.Sites).Include(g => g.Departments)
            .Include(g => g.Constraints).ThenInclude(c => c.Values)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return GroupMutationResult.Fail("Group not found.");
        if (Validate(request) is { } error)
            return GroupMutationResult.Fail(error);

        var name = request.Name.Trim();
        if (await db.SecurityGroups.AnyAsync(g => g.Name == name && g.Id != id, ct))
            return GroupMutationResult.Fail($"A group named '{name}' already exists.");

        // A system group's scope stays fixed; only its description is editable.
        if (!group.IsSystem)
        {
            group.Name = name;
            group.ScopeKind = request.ScopeKind;
            group.IncludeChildSites = request.IncludeChildSites;
            group.Sites.Clear();
            group.Departments.Clear();
            foreach (var siteId in request.SiteIds.Distinct())
                group.Sites.Add(new SecurityGroupSite { SecurityGroupId = id, SiteId = siteId });
            foreach (var departmentId in request.DepartmentIds.Distinct())
                group.Departments.Add(new SecurityGroupDepartment { SecurityGroupId = id, DepartmentId = departmentId });

            group.Constraints.Clear();
            var (ruleKind, constraints) = LegacyScopeMapping.FromLegacy(
                request.ScopeKind, request.IncludeChildSites, request.SiteIds, request.DepartmentIds);
            group.RuleKind = ruleKind;
            foreach (var constraint in constraints)
            {
                constraint.SecurityGroupId = id;
                foreach (var value in constraint.Values) value.SecurityGroupId = id;
                group.Constraints.Add(constraint);
            }
        }
        group.Description = request.Description.Trim();
        group.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(id);
    }

    public async Task<GroupMutationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var group = await db.SecurityGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return GroupMutationResult.Fail("Group not found.");
        if (group.IsSystem)
            return GroupMutationResult.Fail("System groups cannot be deleted.");

        db.SecurityGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(id);
    }

    public async Task<Guid[]> GetUserGroupIdsAsync(Guid userId, CancellationToken ct) =>
        await db.Set<UserSecurityGroup>().AsNoTracking()
            .Where(g => g.UserId == userId)
            .Select(g => g.SecurityGroupId)
            .ToArrayAsync(ct);

    public async Task<GroupMutationResult> SetUserGroupsAsync(Guid userId, Guid[] groupIds, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            return GroupMutationResult.Fail("User not found.");

        var distinct = groupIds.Distinct().ToArray();
        var existing = await db.SecurityGroups.CountAsync(g => distinct.Contains(g.Id), ct);
        if (existing != distinct.Length)
            return GroupMutationResult.Fail("One or more groups do not exist.");

        var current = db.Set<UserSecurityGroup>().Where(g => g.UserId == userId);
        db.RemoveRange(current);
        foreach (var groupId in distinct)
            db.Add(new UserSecurityGroup { UserId = userId, SecurityGroupId = groupId });

        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(userId);
    }

    private static string? Validate(SecurityGroupUpsertRequest request) => request.ScopeKind switch
    {
        DataScopeKind.Sites when request.SiteIds.Length == 0 =>
            "A site-scoped group must include at least one site.",
        DataScopeKind.Departments when request.DepartmentIds.Length == 0 =>
            "A department-scoped group must include at least one department.",
        _ => null,
    };
}
