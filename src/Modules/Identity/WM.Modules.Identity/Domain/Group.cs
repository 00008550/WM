using WM.SharedKernel.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Domain;

/// <summary>
/// A group bundles <em>what its members may do</em> (per-screen read/edit) with
/// <em>which employees they may see</em> (the scope). This mirrors TLW, where the
/// screen at <c>/Groups</c> edits a role carrying both its form permissions and its
/// managed departments/locations/employees.
///
/// Note the naming: TLW's <c>SecurityGroup</c> is a different, physical thing —
/// which employees may open which door readers, synced to hardware. That concept
/// is out of scope for WM, so the name is deliberately not reused here.
///
/// Unlike TLW a user may belong to several groups; access combines as a union.
/// </summary>
public sealed class Group : AuditableEntity
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>System groups cannot be deleted or have their scope changed.</summary>
    public bool IsSystem { get; set; }

    public EmployeeScopeType ScopeType { get; set; } = EmployeeScopeType.None;

    /// <summary>For <see cref="EmployeeScopeType.ByDepartments"/>: include descendant sites.</summary>
    public bool IncludeChildSites { get; set; } = true;

    public List<GroupDepartment> Departments { get; set; } = [];
    public List<GroupSite> Sites { get; set; } = [];
    public List<GroupEmployee> Employees { get; set; } = [];
    public List<GroupScreenPermission> ScreenPermissions { get; set; } = [];
    public List<UserGroup> Members { get; set; } = [];
}

/// <summary>Read or edit rights on one screen. Absent means no access.</summary>
public sealed class GroupScreenPermission
{
    public Guid GroupId { get; set; }
    public required string ScreenId { get; set; }
    public ScreenAccess Access { get; set; }
}

public sealed class GroupDepartment
{
    public Guid GroupId { get; set; }
    public Guid DepartmentId { get; set; }
}

public sealed class GroupSite
{
    public Guid GroupId { get; set; }
    public Guid SiteId { get; set; }
}

/// <summary>An individually named employee, for <see cref="EmployeeScopeType.ByEmployees"/>.</summary>
public sealed class GroupEmployee
{
    public Guid GroupId { get; set; }
    public Guid EmployeeId { get; set; }
}

public sealed class UserGroup
{
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;
}
