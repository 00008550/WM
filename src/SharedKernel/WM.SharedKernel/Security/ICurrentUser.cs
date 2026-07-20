namespace WM.SharedKernel.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    IReadOnlySet<string> Permissions { get; }
    bool HasPermission(string permission);
}
