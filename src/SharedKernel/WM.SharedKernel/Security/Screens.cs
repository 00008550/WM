namespace WM.SharedKernel.Security;

/// <summary>
/// What a group grants on a screen. Mirrors TLW's per-form <c>AccessType</c>,
/// where a role holds none / read / edit against each form in the site structure.
/// </summary>
public enum ScreenAccess
{
    None = 0,
    /// <summary>May open the screen and read its data. Mutating endpoints stay closed.</summary>
    Read = 1,
    /// <summary>May also create, change and delete.</summary>
    Edit = 2,
}

/// <summary>
/// The screens a group can grant access to — WM's equivalent of TLW's
/// <c>SiteStructure</c> form tree. Ids are stable and appear in tokens, so they
/// must not be renamed without a migration.
/// </summary>
public static class WmScreens
{
    public const string Dashboard = "dashboard";
    public const string Employees = "employees";
    public const string Attendance = "attendance";
    public const string Users = "users";
    public const string Groups = "groups";

    public sealed record Definition(string Id, string Name, string Description);

    /// <summary>Drives the permission matrix in the group editor.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(Dashboard, "Dashboard", "Live attendance board and quick punch."),
        new(Employees, "Employees", "Employee records. Edit allows creating and changing them."),
        new(Attendance, "Attendance", "Punches and timesheets. Edit allows recording and correcting."),
        new(Users, "Users", "User accounts. Edit allows creating users and assigning groups."),
        new(Groups, "Groups", "Groups themselves. Edit allows changing who can see and do what."),
    ];

    public static bool IsKnown(string id) => All.Any(s => s.Id == id);
}

/// <summary>
/// How a group decides which employees its members may see.
///
/// Every option is explicit — there is deliberately no "empty list means
/// everything" rule. TLW works that way (<c>if (managedDepartments.Any())</c>),
/// which means a half-configured group silently exposes the whole workforce.
/// Stating the intent makes that impossible.
/// </summary>
public enum EmployeeScopeType
{
    /// <summary>Grants no employee visibility at all.</summary>
    None = 0,
    /// <summary>Every employee. For administrators.</summary>
    AllEmployees = 1,
    /// <summary>Employees in the group's departments and/or sites.</summary>
    ByDepartments = 2,
    /// <summary>An explicit list of employees.</summary>
    ByEmployees = 3,
}
