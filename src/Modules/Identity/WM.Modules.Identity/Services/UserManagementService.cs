using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Common;
using WM.SharedKernel.Security;

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

/// <summary>
/// User CRUD.
///
/// Two of the fields an edit can change are scope inputs, not profile fields: an inactive user
/// resolves to <c>None</c> (<see cref="DataScopeResolver"/>), and the <c>Self</c> grant hangs off
/// <c>EmployeeId</c>. So this service tells <see cref="IScopeChangeNotifier"/> for the same reason
/// <see cref="SecurityGroupService"/> does — an open live connection resolved its audience when it
/// connected and would otherwise keep serving the old one.
/// </summary>
public sealed class UserManagementService(
    IdentityDbContext db, IPasswordHasher<User> hasher, IScopeChangeNotifier scopeChanges)
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
        var email = request.Email.Trim();

        if (userName.Length < 3)
            return UserMutationResult.Fail("Username must be at least 3 characters.");
        if (request.Password.Length < 8)
            return UserMutationResult.Fail("Password must be at least 8 characters.");
        if (email.Length == 0)
            return UserMutationResult.Fail("Email is required.");

        // Case-insensitive: 'admin' and 'Admin' must not both exist, or you get two
        // accounts that look identical to a human.
        if (await db.Users.AnyAsync(u => u.UserName.ToLower() == userName.ToLower(), ct))
            return UserMutationResult.Fail($"Username '{userName}' is already taken.");
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
            return UserMutationResult.Fail($"Email '{email}' is already in use.");
        if (request.EmployeeId is { } eid && await db.Users.AnyAsync(u => u.EmployeeId == eid, ct))
            return UserMutationResult.Fail("That employee is already linked to another user.");

        var roles = await ResolveRolesAsync(request.RoleIds, ct);
        if (roles is null)
            return UserMutationResult.Fail("One or more roles do not exist.");

        var user = new User
        {
            UserName = userName,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            EmployeeId = request.EmployeeId,
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.Roles = roles.Select(r => new UserRole { UserId = user.Id, RoleId = r.Id, Role = r }).ToList();

        db.Users.Add(user);
        return await SaveGuardingUniquenessAsync(user.Id, ct);
    }

    /// <summary>
    /// Saves and converts a unique-index violation into a readable message. The
    /// checks above race with concurrent requests, so the database constraint is
    /// the real guarantee — this stops it surfacing as a 500.
    /// </summary>
    private async Task<UserMutationResult> SaveGuardingUniquenessAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return UserMutationResult.Ok(id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            var field = pg.ConstraintName?.Contains("Email", StringComparison.OrdinalIgnoreCase) == true
                ? "email"
                : "username";
            return UserMutationResult.Fail($"That {field} is already in use.");
        }
    }

    public async Task<UserMutationResult> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return UserMutationResult.Fail("User not found.");

        var email = request.Email.Trim();
        if (email.Length == 0)
            return UserMutationResult.Fail("Email is required.");
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower() && u.Id != id, ct))
            return UserMutationResult.Fail($"Email '{email}' is already in use.");
        if (request.EmployeeId is { } eid && await db.Users.AnyAsync(u => u.EmployeeId == eid && u.Id != id, ct))
            return UserMutationResult.Fail("That employee is already linked to another user.");

        var roles = await ResolveRolesAsync(request.RoleIds, ct);
        if (roles is null)
            return UserMutationResult.Fail("One or more roles do not exist.");

        // Measured against the loaded row, before the assignments below overwrite it: a
        // display-name or email edit moves nobody's scope and must not churn every socket the
        // user holds.
        var scopeChanged = user.IsActive != request.IsActive || user.EmployeeId != request.EmployeeId;

        user.DisplayName = request.DisplayName.Trim();
        user.Email = email;
        user.IsActive = request.IsActive;
        user.EmployeeId = request.EmployeeId;
        user.Roles.Clear();
        user.Roles.AddRange(roles.Select(r => new UserRole { UserId = user.Id, RoleId = r.Id }));
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await SaveGuardingUniquenessAsync(user.Id, ct);
        // Only after the write lands. A rejected save leaves the database as it was, so nothing
        // has moved and there is nothing to re-group.
        if (result.Succeeded && scopeChanged)
            await scopeChanges.UserScopeChangedAsync([user.Id], ct);

        return result;
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
