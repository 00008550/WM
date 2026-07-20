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
}
