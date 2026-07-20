using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Common;

namespace WM.Modules.Identity.Services;

public sealed record UserListItem(
    Guid Id, string UserName, string Email, string DisplayName,
    bool IsActive, bool IsLockedOut, Guid? EmployeeId, string[] Roles);

public sealed record RoleListItem(Guid Id, string Name, string Description, bool IsSystem);

public sealed record CreateUserRequest(
    string UserName, string Email, string DisplayName, string Password,
    Guid? EmployeeId, Guid[] RoleIds);

public sealed record UpdateUserRequest(
    string DisplayName, string Email, bool IsActive, Guid? EmployeeId, Guid[] RoleIds);

public sealed record UserMutationResult(bool Succeeded, string? Error, Guid? UserId)
{
    public static UserMutationResult Ok(Guid id) => new(true, null, id);
    public static UserMutationResult Fail(string error) => new(false, error, null);
}

public sealed class UserManagementService(IdentityDbContext db, IPasswordHasher<User> hasher)
{
    public async Task<PagedResult<UserListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.UserName, pattern) ||
                EF.Functions.ILike(u.Email, pattern) ||
                EF.Functions.ILike(u.DisplayName, pattern));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = users.Select(ToListItem).ToList();
        return new PagedResult<UserListItem>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<RoleListItem>> ListRolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleListItem(r.Id, r.Name, r.Description, r.IsSystem))
            .ToListAsync(ct);

    public async Task<UserMutationResult> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var userName = request.UserName.Trim();
        if (userName.Length < 3)
            return UserMutationResult.Fail("Username must be at least 3 characters.");
        if (request.Password.Length < 8)
            return UserMutationResult.Fail("Password must be at least 8 characters.");
        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
            return UserMutationResult.Fail($"Username '{userName}' is already taken.");
        if (request.EmployeeId is { } eid && await db.Users.AnyAsync(u => u.EmployeeId == eid, ct))
            return UserMutationResult.Fail("That employee is already linked to another user.");

        var roles = await ResolveRolesAsync(request.RoleIds, ct);
        if (roles is null)
            return UserMutationResult.Fail("One or more roles do not exist.");

        var user = new User
        {
            UserName = userName,
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmployeeId = request.EmployeeId,
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.Roles = roles.Select(r => new UserRole { UserId = user.Id, RoleId = r.Id, Role = r }).ToList();

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return UserMutationResult.Ok(user.Id);
    }

    public async Task<UserMutationResult> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return UserMutationResult.Fail("User not found.");
        if (request.EmployeeId is { } eid && await db.Users.AnyAsync(u => u.EmployeeId == eid && u.Id != id, ct))
            return UserMutationResult.Fail("That employee is already linked to another user.");

        var roles = await ResolveRolesAsync(request.RoleIds, ct);
        if (roles is null)
            return UserMutationResult.Fail("One or more roles do not exist.");

        user.DisplayName = request.DisplayName.Trim();
        user.Email = request.Email.Trim();
        user.IsActive = request.IsActive;
        user.EmployeeId = request.EmployeeId;
        user.Roles.Clear();
        user.Roles.AddRange(roles.Select(r => new UserRole { UserId = user.Id, RoleId = r.Id }));
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return UserMutationResult.Ok(user.Id);
    }

    public async Task<UserMutationResult> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct)
    {
        if (newPassword.Length < 8)
            return UserMutationResult.Fail("Password must be at least 8 characters.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return UserMutationResult.Fail("User not found.");

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;
        // Revoke every active session so the old password can't be used via a live refresh token.
        await db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        await db.SaveChangesAsync(ct);
        return UserMutationResult.Ok(id);
    }

    private async Task<List<Role>?> ResolveRolesAsync(Guid[] roleIds, CancellationToken ct)
    {
        var distinct = roleIds.Distinct().ToArray();
        var roles = await db.Roles.Where(r => distinct.Contains(r.Id)).ToListAsync(ct);
        return roles.Count == distinct.Length ? roles : null;
    }

    private static UserListItem ToListItem(User u) => new(
        u.Id, u.UserName, u.Email, u.DisplayName, u.IsActive, u.IsLockedOut, u.EmployeeId,
        u.Roles.Select(r => r.Role.Name).OrderBy(n => n).ToArray());
}
