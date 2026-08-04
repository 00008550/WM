namespace WM.SharedKernel.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    /// <summary>The employee this user is linked to, if any. Drives self-service scoping.</summary>
    Guid? EmployeeId { get; }
    bool IsAuthenticated { get; }
    IReadOnlySet<string> Permissions { get; }
    bool HasPermission(string permission);
}

public static class WmClaims
{
    /// <summary>Linked employee id, present only for employee-linked users.</summary>
    public const string EmployeeId = "wm:eid";

    /// <summary>
    /// The subject of a WM access token. JwtBearer maps <c>sub</c> onto
    /// <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/> under the default claim
    /// mapping but not under every configuration, so both are read.
    ///
    /// Lives here rather than only inside the HTTP <see cref="ICurrentUser"/> because a
    /// transport can have a principal and no <c>HttpContext</c> — a SignalR hub does — and two
    /// copies of "who is this token" is exactly the kind of drift that lets one transport
    /// authorize differently from another.
    /// </summary>
    public static Guid? UserIdOf(System.Security.Claims.ClaimsPrincipal? principal) =>
        Guid.TryParse(
            principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id)
            ? id
            : null;
}
