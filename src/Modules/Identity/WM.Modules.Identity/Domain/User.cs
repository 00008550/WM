using WM.SharedKernel.Domain;

namespace WM.Modules.Identity.Domain;

public sealed class User : AuditableEntity
{
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedOutUntil { get; set; }
    public Guid? EmployeeId { get; set; }

    public List<UserRole> Roles { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];

    public bool IsLockedOut => LockedOutUntil.HasValue && LockedOutUntil.Value > DateTimeOffset.UtcNow;
}

public sealed class Role : Entity
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public required string Permission { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    /// <summary>SHA-256 of the token value; the raw token is never stored.</summary>
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>Hash of the token that replaced this one — used for reuse detection.</summary>
    public string? ReplacedByHash { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
