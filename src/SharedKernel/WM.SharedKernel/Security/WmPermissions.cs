namespace WM.SharedKernel.Security;

/// <summary>
/// Central registry of fine-grained permissions. Roles are bundles of these;
/// endpoints require them via authorization policies of the same name.
/// </summary>
public static class WmPermissions
{
    public const string ClaimType = "wm:perm";

    // People
    public const string EmployeesView = "employees.view";
    public const string EmployeesManage = "employees.manage";
    public const string SitesManage = "sites.manage";

    // Time & attendance
    public const string AttendanceView = "attendance.view";
    public const string PunchesRecord = "punches.record";
    public const string TimesheetsEdit = "timesheets.edit";

    // Self-service — every employee-linked user has this; scoped to their own record only.
    public const string SelfService = "selfservice.access";

    // Administration
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string LicenseView = "license.view";
    public const string PluginsManage = "plugins.manage";

    public static readonly IReadOnlyList<string> All =
    [
        EmployeesView, EmployeesManage, SitesManage,
        AttendanceView, PunchesRecord, TimesheetsEdit,
        SelfService,
        UsersManage, RolesManage, LicenseView, PluginsManage,
    ];
}
