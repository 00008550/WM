using WM.SharedKernel.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Domain;

/// <summary>
/// A named bundle of data visibility. Users are added to groups; the groups decide
/// which employees they can see.
///
/// Deliberately orthogonal to <see cref="Role"/>: roles say what a user may *do*
/// (permissions), groups say which records they may *see*. Legacy conflated the two
/// across Role, SecurityGroup, FormAccess and SiteItemPermission, which is why it
/// needed a diagnostics subsystem to explain itself.
/// </summary>
public sealed class SecurityGroup : AuditableEntity
{
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>System groups (e.g. "All employees") cannot be deleted.</summary>
    public bool IsSystem { get; set; }

    public DataScopeKind ScopeKind { get; set; } = DataScopeKind.None;

    /// <summary>For <see cref="DataScopeKind.Sites"/>: also include descendant sites.</summary>
    public bool IncludeChildSites { get; set; } = true;

    public List<SecurityGroupSite> Sites { get; set; } = [];
    public List<SecurityGroupDepartment> Departments { get; set; } = [];
    public List<UserSecurityGroup> Members { get; set; } = [];
}

public sealed class SecurityGroupSite
{
    public Guid SecurityGroupId { get; set; }
    public Guid SiteId { get; set; }
}

public sealed class SecurityGroupDepartment
{
    public Guid SecurityGroupId { get; set; }
    public Guid DepartmentId { get; set; }
}

public sealed class UserSecurityGroup
{
    public Guid UserId { get; set; }
    public Guid SecurityGroupId { get; set; }
    public SecurityGroup SecurityGroup { get; set; } = null!;
}
