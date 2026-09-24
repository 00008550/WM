using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;

namespace WM.Modules.Identity.Services;

public sealed record AuthResult(bool Succeeded, string? Error, TokenPair? Tokens, User? User);

/// <summary>
/// How many failures lock an account, and for how long.
///
/// <para>
/// These were two <c>const</c>s until 006 P3. Lockout is policy, and the two installs that care
/// pull in opposite directions: a public demo whose credentials are printed on the internet locks
/// itself out on the first credential-stuffing bot unless it is lenient, while a customer may want
/// it stricter than five. The defaults are exactly what shipped, so behaviour is unchanged unless
/// a deployment says otherwise.
/// </para>
///
/// <para>
/// Named <c>Account…</c> rather than <c>LockoutOptions</c> because ASP.NET Identity ships a type
/// of that name which WM does not use — the sign-in path here is <see cref="AuthService"/>'s own,
/// and two identically-named options types in one file would invite someone to configure the
/// wrong one.
/// </para>
/// </summary>
public sealed class AccountLockoutOptions
{
    public const string SectionName = "Lockout";

    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// One line saying why this configuration cannot be used, or <c>null</c> when it can — the
    /// same shape as <see cref="JwtOptions.DescribeSigningKeyFault"/>, and read at composition for
    /// the same reason: a host that would lock every account on the first typo should not start.
    /// </summary>
    public static string? DescribeFault(AccountLockoutOptions options)
    {
        if (options.MaxFailedAttempts < 1)
            return $"{SectionName}:MaxFailedAttempts is {options.MaxFailedAttempts}, which would "
                   + "lock an account on its first failed sign-in. Set it (env: "
                   + "Lockout__MaxFailedAttempts) to at least 1. There is deliberately no value "
                   + "that turns lockout off: 0 reads as both 'never lock' and 'lock immediately', "
                   + "and a setting whose meaning has to be guessed is not a safety control.";

        if (options.Duration <= TimeSpan.Zero)
            return $"{SectionName}:Duration is {options.Duration}, so a locked account would be "
                   + "unlocked again before it could be told. Set it (env: Lockout__Duration) to a "
                   + "duration such as 00:15:00.";

        return null;
    }
}

public sealed class AuthService(
    IdentityDbContext db,
    TokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IOptions<AccountLockoutOptions> lockout,
    ILogger<AuthService> logger)
{
    private readonly AccountLockoutOptions _lockout = lockout.Value;

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
            if (user.FailedLoginAttempts >= _lockout.MaxFailedAttempts)
            {
                user.LockedOutUntil = DateTimeOffset.UtcNow.Add(_lockout.Duration);
                user.FailedLoginAttempts = 0;
                logger.LogWarning(
                    "User {User} locked out for {Duration} after {Attempts} failed attempts",
                    user.UserName, _lockout.Duration, _lockout.MaxFailedAttempts);
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

    /// <summary>
    /// Development-only sign-in with no password (plan 011 P9). Tokens come from the same
    /// <see cref="IssueTokens"/> as <see cref="LoginAsync"/>, so the refresh token is hashed and
    /// stored and refresh, rotation and logout behave identically. Unknown and inactive users get
    /// the same answer, so this is not an enumeration oracle. Only reachable when
    /// <see cref="DevSignInOptions.ShouldMap"/> mapped the route.
    /// </summary>
    public async Task<AuthResult> DevSignInAsync(string userName, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Roles).ThenInclude(r => r.Role).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.UserName == userName || u.Email == userName, ct);

        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Development sign-in refused for {User}", userName);
            return new AuthResult(false, "Invalid credentials.", null, null);
        }

        logger.LogWarning("Development sign-in used for {User} — no password was checked", user.UserName);

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
