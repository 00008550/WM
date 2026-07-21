using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

public sealed record AuthResult(bool Succeeded, string? Error, TokenPair? Tokens, User? User);

public sealed class AuthService(
    IdentityDbContext db,
    TokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IDataScopeResolver scopes,
    ILogger<AuthService> logger)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult> LoginAsync(string userName, string password, CancellationToken ct)
    {
        var user = await db.Users
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

        var pair = await IssueTokensAsync(user, ct);
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
            .FirstAsync(u => u.Id == stored.UserId, ct);

        if (!user.IsActive)
            return new AuthResult(false, "Account disabled.", null, null);

        var pair = await IssueTokensAsync(user, ct);
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

    private async Task<TokenPair> IssueTokensAsync(User user, CancellationToken ct)
    {
        // Screen rights come from the user's groups, resolved at sign-in so a
        // group change takes effect on the next token rather than immediately —
        // the same trade-off as any claims-based token.
        var access = await scopes.GetScreenAccessForUserAsync(user.Id, ct);
        var pair = tokens.CreateTokenPair(user, access);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashToken(pair.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiry(),
        });
        return pair;
    }
}
