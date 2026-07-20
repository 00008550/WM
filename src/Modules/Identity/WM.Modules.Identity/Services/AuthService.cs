using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;

namespace WM.Modules.Identity.Services;

public sealed record AuthResult(bool Succeeded, string? Error, TokenPair? Tokens, User? User);

public sealed class AuthService(
    IdentityDbContext db,
    TokenService tokens,
    IPasswordHasher<User> passwordHasher,
    ILogger<AuthService> logger)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Roles).ThenInclude(r => r.Role).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.UserName == userName || u.Email == userName, ct);

        if (user is null || !user.IsActive)
            return new AuthResult(false, "Invalid credentials.", null, null);

        if (user.IsLockedOut)
            return new AuthResult(false, "Account is temporarily locked. Try again later.", null, null);

        var verdict = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedOutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
                logger.LogWarning("User {User} locked out after repeated failures", user.UserName);
            }
            await db.SaveChangesAsync(ct);
            return new AuthResult(false, "Invalid credentials.", null, null);
        }

        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, password);

        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;

        var pair = IssueTokens(user);
        await db.SaveChangesAsync(ct);
        return new AuthResult(true, null, pair, user);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            return new AuthResult(false, "Invalid refresh token.", null, null);

        if (!stored.IsActive)
        {
            // Reuse of a rotated/revoked token → assume theft, revoke the whole family.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", stored.UserId);
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
            return new AuthResult(false, "Invalid refresh token.", null, null);
        }

        var user = await db.Users
            .Include(u => u.Roles).ThenInclude(r => r.Role).ThenInclude(r => r.Permissions)
            .FirstAsync(u => u.Id == stored.UserId, ct);

        if (!user.IsActive)
            return new AuthResult(false, "Account disabled.", null, null);

        var pair = IssueTokens(user);
        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.ReplacedByHash = TokenService.HashToken(pair.RefreshToken);
        await db.SaveChangesAsync(ct);
        return new AuthResult(true, null, pair, user);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.HashToken(refreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    public static IReadOnlyCollection<string> PermissionsOf(User user) =>
        user.Roles.SelectMany(r => r.Role.Permissions).Select(p => p.Permission).ToHashSet();

    private TokenPair IssueTokens(User user)
    {
        var pair = tokens.CreateTokenPair(user, PermissionsOf(user));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashToken(pair.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiry(),
        });
        return pair;
    }
}
