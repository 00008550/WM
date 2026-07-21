using WM.SharedKernel.Domain;

namespace WM.Modules.Identity.Domain;

/// <summary>
/// A sign-in account. What the user may do and see comes entirely from their
/// <see cref="Group"/> membership — there is no separate role concept, matching
/// TLW where the group screen edits one object carrying both.
/// </summary>
public sealed class User : AuditableEntity
{
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedOutUntil { get; set; }

    /// <summary>Links the account to an employee, enabling self-service.</summary>
    public Guid? EmployeeId { get; set; }

    public List<UserGroup> Groups { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];

    public bool IsLockedOut => LockedOutUntil.HasValue && LockedOutUntil.Value > DateTimeOffset.UtcNow;
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
