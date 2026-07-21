namespace WM.SharedKernel.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    /// <summary>The employee this user is linked to, if any. Drives self-service scoping.</summary>
    Guid? EmployeeId { get; }
    bool IsAuthenticated { get; }

    /// <summary>Access level this user holds on a screen, combined across their groups.</summary>
    ScreenAccess AccessTo(string screenId);

    bool CanRead(string screenId) => AccessTo(screenId) >= ScreenAccess.Read;
    bool CanEdit(string screenId) => AccessTo(screenId) >= ScreenAccess.Edit;
}

public static class WmClaims
{
    /// <summary>Linked employee id, present only for employee-linked users.</summary>
    public const string EmployeeId = "wm:eid";

    /// <summary>
    /// One claim per screen the user may touch, valued "screenId:level" — for
    /// example "employees:edit". Absent means no access to that screen.
    /// </summary>
    public const string Screen = "wm:screen";

    public static string ScreenValue(string screenId, ScreenAccess access) =>
        $"{screenId}:{access.ToString().ToLowerInvariant()}";

    /// <summary>Policy name for requiring read on a screen.</summary>
    public static string ReadPolicy(string screenId) => $"screen:{screenId}:read";

    /// <summary>Policy name for requiring edit on a screen.</summary>
    public static string EditPolicy(string screenId) => $"screen:{screenId}:edit";
}
