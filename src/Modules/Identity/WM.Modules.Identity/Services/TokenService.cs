using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The signing key is used as its UTF-8 bytes (<see cref="TokenService.CreateTokenPair"/> and
    /// <c>IdentityModule.RegisterServices</c> both call <see cref="Encoding.GetBytes(string)"/>),
    /// so the minimum is measured in <em>bytes</em> and not characters — 32 characters of
    /// non-ASCII text is more than 32 bytes, and 32 bytes is what HMAC-SHA256 needs. Anything
    /// shorter is not merely weak: the JWT handler refuses it (IDX10653) at the first sign-in,
    /// which is a runtime 500 rather than a boot failure.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>
    /// Every signing key this repository publishes. Outside Development a host refuses to boot on
    /// one, because "published in a public-ish git history" and "signs administrator tokens" are
    /// not compatible. Listing them here is safe and deliberate — nothing reads this as a default,
    /// it is only ever compared against.
    /// <list type="bullet">
    ///   <item>the first shipped in <c>appsettings.json</c>, and therefore inside the
    ///   <c>wm-api</c> image, until plan 006 P2 removed it;</item>
    ///   <item>the second replaced it in <c>appsettings.Development.json</c>, which
    ///   <c>.dockerignore</c> keeps out of the image. It is listed too so that copying the
    ///   developer file's value onto a server is refused rather than quietly accepted.</item>
    /// </list>
    /// </summary>
    public static readonly string[] PublishedSigningKeys =
    [
        "dev-only-signing-key-change-me-0123456789abcdef",
        "local-development-only-not-a-secret-do-not-deploy",
    ];

    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    /// <summary>HMAC signing key, min 32 bytes. Provide via env/user-secrets in real deployments.</summary>
    public required string SigningKey { get; set; }
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// One line saying why this key cannot be used, or <c>null</c> when it can.
    /// <para>
    /// Missing and too-short are refused everywhere, because such a key cannot sign a token in any
    /// environment. A <em>published</em> key is refused only outside Development, which is what
    /// keeps <c>dotnet run</c> working with no environment variables set.
    /// </para>
    /// <para>
    /// The text names the setting and how to fix it, and deliberately echoes neither the
    /// configured value nor an expected one: it is written to the log of a host that is about to
    /// crash-loop, and whatever collects those logs is not necessarily trusted with keys.
    /// </para>
    /// </summary>
    public static string? DescribeSigningKeyFault(string? signingKey, bool isDevelopment)
    {
        const string fix = "Set it (env: Jwt__SigningKey) to a unique random value of at least "
                           + "32 bytes — e.g. `openssl rand -base64 48`.";

        if (string.IsNullOrWhiteSpace(signingKey))
            return $"Jwt:SigningKey is not configured, so this host cannot sign tokens. {fix}";

        if (Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
            return $"Jwt:SigningKey is shorter than {MinimumSigningKeyBytes} bytes, which "
                   + $"HMAC-SHA256 cannot sign with. {fix}";

        if (!isDevelopment && PublishedSigningKeys.Contains(signingKey, StringComparer.Ordinal))
            return "Jwt:SigningKey is one of the development keys published in the WM repository, "
                   + $"so anyone holding a copy could mint tokens for this host. {fix}";

        return null;
    }
}

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public TokenPair CreateTokenPair(User user, IReadOnlyCollection<string> permissions)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.Name)));
        claims.AddRange(permissions.Select(p => new Claim(WmPermissions.ClaimType, p)));
        if (user.EmployeeId is { } employeeId)
            claims.Add(new Claim(WmClaims.EmployeeId, employeeId.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            expires,
            GenerateRefreshTokenValue());
    }

    public DateTimeOffset RefreshTokenExpiry() => DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);

    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string GenerateRefreshTokenValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
