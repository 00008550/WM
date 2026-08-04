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

    // ---- legacy single-kind shape (plan 001 P3 deletes this half) ----
    // Still authoritative: the resolver and query filter read these until P3 moves over.
    // Kept alongside the composed shape below so P2 changes no behaviour and the migration
    // can be validated against today's semantics rather than a moving baseline.

    public DataScopeKind ScopeKind { get; set; } = DataScopeKind.None;

    /// <summary>For <see cref="DataScopeKind.Sites"/>: also include descendant sites.</summary>
    public bool IncludeChildSites { get; set; } = true;

    public List<SecurityGroupSite> Sites { get; set; } = [];
    public List<SecurityGroupDepartment> Departments { get; set; } = [];

    // ---- composed shape (plan 001) ----

    /// <summary>
    /// What this group grants. <see cref="ScopeRuleKind.Constrained"/> means "every constraint
    /// in <see cref="Constraints"/> must hold" — an intersection.
    /// </summary>
    public ScopeRuleKind RuleKind { get; set; } = ScopeRuleKind.None;

    /// <summary>
    /// One row per dimension this group narrows on. Absent dimension = unconstrained;
    /// present with zero values = matches nobody. That inversion of legacy's fail-open
    /// is the point, so it must survive persistence.
    /// </summary>
    public List<SecurityGroupConstraint> Constraints { get; set; } = [];

    public List<UserSecurityGroup> Members { get; set; } = [];
}

/// <summary>One dimension's constraint on a group. Values live in the child table.</summary>
public sealed class SecurityGroupConstraint
{
    public Guid SecurityGroupId { get; set; }
    public ScopeDimension Dimension { get; set; }

    /// <summary>
    /// Expand <see cref="Values"/> to descendants at resolution time (site trees).
    /// Per-dimension rather than per-group: legacy's single OR'd <c>IncludeChildSites</c> flag
    /// let one group's setting expand a different group's sites.
    /// </summary>
    public bool IncludeDescendants { get; set; }

    public List<SecurityGroupConstraintValue> Values { get; set; } = [];
}

public sealed class SecurityGroupConstraintValue
{
    public Guid SecurityGroupId { get; set; }
    public ScopeDimension Dimension { get; set; }
    public Guid ValueId { get; set; }
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
