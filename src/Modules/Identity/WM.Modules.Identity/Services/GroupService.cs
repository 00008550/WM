using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

public sealed record ScreenPermissionDto(string ScreenId, ScreenAccess Access);

public sealed record GroupListItem(
    Guid Id, string Name, string Description, bool IsSystem,
    EmployeeScopeType ScopeType, bool IncludeChildSites,
    Guid[] DepartmentIds, Guid[] SiteIds, Guid[] EmployeeIds,
    ScreenPermissionDto[] Screens, int MemberCount);

public sealed record GroupUpsertRequest(
    string Name, string Description,
    EmployeeScopeType ScopeType, bool IncludeChildSites,
    Guid[] DepartmentIds, Guid[] SiteIds, Guid[] EmployeeIds,
    ScreenPermissionDto[] Screens);

public sealed record GroupMutationResult(bool Succeeded, string? Error, Guid? Id)
{
    public static GroupMutationResult Ok(Guid id) => new(true, null, id);
    public static GroupMutationResult Fail(string error) => new(false, error, null);
}

public sealed class GroupService(IdentityDbContext db)
{
    public async Task<IReadOnlyList<GroupListItem>> ListAsync(CancellationToken ct) =>
        await db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupListItem(
                g.Id, g.Name, g.Description, g.IsSystem, g.ScopeType, g.IncludeChildSites,
                g.Departments.Select(d => d.DepartmentId).ToArray(),
                g.Sites.Select(s => s.SiteId).ToArray(),
                g.Employees.Select(e => e.EmployeeId).ToArray(),
                g.ScreenPermissions
                    .Where(p => p.Access != ScreenAccess.None)
                    .Select(p => new ScreenPermissionDto(p.ScreenId, p.Access)).ToArray(),
                g.Members.Count))
            .ToListAsync(ct);

    public async Task<GroupMutationResult> CreateAsync(GroupUpsertRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length < 2)
            return GroupMutationResult.Fail("Group name must be at least 2 characters.");
        if (await db.Groups.AnyAsync(g => g.Name.ToLower() == name.ToLower(), ct))
            return GroupMutationResult.Fail($"A group named '{name}' already exists.");
        if (Validate(request) is { } error)
            return GroupMutationResult.Fail(error);

        var group = new Group
        {
            Name = name,
            Description = request.Description.Trim(),
            ScopeType = request.ScopeType,
            IncludeChildSites = request.IncludeChildSites,
        };
        ApplyScope(group, request);
        ApplyScreens(group, request);

        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(group.Id);
    }

    public async Task<GroupMutationResult> UpdateAsync(Guid id, GroupUpsertRequest request, CancellationToken ct)
    {
        var group = await db.Groups
            .Include(g => g.Departments).Include(g => g.Sites)
            .Include(g => g.Employees).Include(g => g.ScreenPermissions)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return GroupMutationResult.Fail("Group not found.");
        if (Validate(request) is { } error)
            return GroupMutationResult.Fail(error);

        var name = request.Name.Trim();
        if (await db.Groups.AnyAsync(g => g.Name.ToLower() == name.ToLower() && g.Id != id, ct))
            return GroupMutationResult.Fail($"A group named '{name}' already exists.");

        // A system group's scope is fixed; its screen rights and description are not.
        if (!group.IsSystem)
        {
            group.Name = name;
            group.ScopeType = request.ScopeType;
            group.IncludeChildSites = request.IncludeChildSites;
            group.Departments.Clear();
            group.Sites.Clear();
            group.Employees.Clear();
            ApplyScope(group, request);
        }
        group.Description = request.Description.Trim();
        group.ScreenPermissions.Clear();
        ApplyScreens(group, request);
        group.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(id);
    }

    public async Task<GroupMutationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return GroupMutationResult.Fail("Group not found.");
        if (group.IsSystem)
            return GroupMutationResult.Fail("System groups cannot be deleted.");

        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(id);
    }

    public async Task<Guid[]> GetUserGroupIdsAsync(Guid userId, CancellationToken ct) =>
        await db.Set<UserGroup>().AsNoTracking()
            .Where(g => g.UserId == userId).Select(g => g.GroupId).ToArrayAsync(ct);

    public async Task<GroupMutationResult> SetUserGroupsAsync(Guid userId, Guid[] groupIds, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            return GroupMutationResult.Fail("User not found.");

        var distinct = groupIds.Distinct().ToArray();
        if (await db.Groups.CountAsync(g => distinct.Contains(g.Id), ct) != distinct.Length)
            return GroupMutationResult.Fail("One or more groups do not exist.");

        db.RemoveRange(db.Set<UserGroup>().Where(g => g.UserId == userId));
        foreach (var groupId in distinct)
            db.Add(new UserGroup { UserId = userId, GroupId = groupId });

        await db.SaveChangesAsync(ct);
        return GroupMutationResult.Ok(userId);
    }

    private static void ApplyScope(Group group, GroupUpsertRequest request)
    {
        switch (request.ScopeType)
        {
            case EmployeeScopeType.ByDepartments:
                foreach (var id in request.DepartmentIds.Distinct())
                    group.Departments.Add(new GroupDepartment { GroupId = group.Id, DepartmentId = id });
                foreach (var id in request.SiteIds.Distinct())
                    group.Sites.Add(new GroupSite { GroupId = group.Id, SiteId = id });
                break;
            case EmployeeScopeType.ByEmployees:
                foreach (var id in request.EmployeeIds.Distinct())
                    group.Employees.Add(new GroupEmployee { GroupId = group.Id, EmployeeId = id });
                break;
        }
    }

    private static void ApplyScreens(Group group, GroupUpsertRequest request)
    {
        foreach (var screen in request.Screens.Where(s => s.Access != ScreenAccess.None))
            group.ScreenPermissions.Add(new GroupScreenPermission
            {
                GroupId = group.Id,
                ScreenId = screen.ScreenId,
                Access = screen.Access,
            });
    }

    private static string? Validate(GroupUpsertRequest request)
    {
        if (request.Screens.Any(s => !WmScreens.IsKnown(s.ScreenId)))
            return "One or more screens are not recognised.";

        return request.ScopeType switch
        {
            EmployeeScopeType.ByDepartments when request.DepartmentIds.Length == 0 && request.SiteIds.Length == 0 =>
                "Choose at least one department or site, or change the scope to 'No employee data'.",
            EmployeeScopeType.ByEmployees when request.EmployeeIds.Length == 0 =>
                "Choose at least one employee, or change the scope to 'No employee data'.",
            _ => null,
        };
    }
}
