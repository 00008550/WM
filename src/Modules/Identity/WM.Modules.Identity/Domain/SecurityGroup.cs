using WM.SharedKernel.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Domain;

/// <summary>
/// A named bundle of data visibility. Users are added to groups; the groups decide
/// which employees they can see.
///
/// Today a group is separate from <see cref="Role"/>, which carries permissions. That split is a
/// state of the code, not a principle: ARCHITECTURE.md §4 (ruled 2026-08-05) puts rights and
/// scope in <em>one</em> object with <em>one</em> membership per user, which is what legacy does —
/// <c>/Groups</c> edits a single <c>dbo.Role</c> and a user holds exactly one of them
/// (<c>AuthorizationService.cs:161</c>). Plan 005 merges the two here; until it lands, treat the
/// separation as legacy-of-WM rather than design.
///
/// This comment previously said the split was "deliberately orthogonal" because legacy had
/// conflated the two "across Role, SecurityGroup, FormAccess and SiteItemPermission, which is why
/// it needed a diagnostics subsystem to explain itself". Measured 2026-08-05, none of that holds:
/// <c>FormAccess</c> is a static helper class and <c>SiteItemPermission</c> an in-memory DTO
/// (neither is a table to conflate anything across), and <c>DataAccessScopeDiagnostics</c> counts
/// <c>DataContext</c> create/dispose to find connection leaks. See TLW-AUTHORIZATION-MODEL.md §1
/// and §11 C2.
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
