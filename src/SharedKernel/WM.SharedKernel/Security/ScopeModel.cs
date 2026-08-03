namespace WM.SharedKernel.Security;

// The compositional replacement for DataScopeKind / EffectiveDataScope (see DataScope.cs).
//
// The old model gives a security group a single ScopeKind, so a group is *either* site-scoped
// *or* department-scoped. Legacy supports both at once — RoleBasedEmployeeFilterService applies
// managed departments and then managed locations as successive Where() calls, an intersection —
// and "departments A and B, but only at site C" is a configuration customers actually run.
// Resolution across groups is also lossy today: one Kind wins, so a user in a site group and a
// department group silently loses the departments they were granted.
//
// This model fixes the shape: constraints intersect within a rule, rules union across a user's
// groups. Nothing consumes it yet — plan 001 P3 moves the resolver and the query filter over and
// deletes the legacy pair. Introduced separately so P1 changes no behaviour and P2's migration
// can be validated against today's semantics rather than a moving target.
//
// See docs/plans/001-compositional-data-scope.md.

/// <summary>
/// A structural axis along which a group can narrow employee visibility.
///
/// Legacy exposes six <c>Managed*ByRole</c> accessors; <see cref="CostCentre"/> and
/// <see cref="WorkActivity"/> are declared here so the shape does not have to change when their
/// entities arrive (Rules, phase 2; Activities, phase 7), but nothing resolves them yet. Caterers
/// are not modelled — they belong to the dropped EPOS vertical.
/// </summary>
public enum ScopeDimension
{
    Site = 0,
    Department = 1,
    Building = 2,

    /// <summary>An explicit list of employees — legacy's <c>ByEmployees</c> management type.</summary>
    Employee = 3,

    /// <summary>Reserved: entity arrives with the Rules module (phase 2). Not yet resolvable.</summary>
    CostCentre = 4,

    /// <summary>Reserved: entity arrives with the Activities module (phase 7). Not yet resolvable.</summary>
    WorkActivity = 5,
}

/// <summary>
/// The employee-side values a rule is matched against. One per employee being tested.
///
/// Nullable members are deliberate: an employee may have no department, no building. A null
/// value never satisfies a constraint — see <see cref="ScopeConstraint.Matches"/>.
/// </summary>
public sealed record ScopeSubject(
    Guid EmployeeId,
    Guid SiteId,
    Guid? DepartmentId = null,
    Guid? BuildingId = null)
{
    /// <summary>
    /// The employee's value on a given axis, or null if they have none.
    /// Adding a dimension means adding a member above and an arm here — the rule model itself
    /// does not change.
    /// </summary>
    public Guid? ValueFor(ScopeDimension dimension) => dimension switch
    {
        ScopeDimension.Site => SiteId,
        ScopeDimension.Department => DepartmentId,
        ScopeDimension.Building => BuildingId,
        ScopeDimension.Employee => EmployeeId,
        // Declared but not resolvable yet. Returning null means a group constrained on one of
        // these matches nobody, which is the correct fail-closed answer until the entity exists.
        _ => null,
    };
}

/// <summary>
/// One dimension's constraint within a rule: "this axis must be one of these ids".
///
/// Not a record: it holds a set, and record equality would compare that set by reference, which
/// is a quietly wrong contract for a security primitive.
/// </summary>
public sealed class ScopeConstraint
{
    public ScopeConstraint(ScopeDimension dimension, IEnumerable<Guid> ids, bool includeDescendants = false)
    {
        Dimension = dimension;
        // Always copy. A HashSet<Guid> satisfies IReadOnlySet<Guid>, so storing the caller's
        // instance would let them keep a mutable handle on a security constraint and widen it
        // after the fact — and the resolver does build its id sets incrementally before
        // expanding them for child sites.
        Ids = new HashSet<Guid>(ids);
        IncludeDescendants = includeDescendants;
    }

    public ScopeDimension Dimension { get; }

    /// <summary>
    /// The permitted ids. **Empty means nobody**, never everybody — the deliberate inversion of
    /// legacy's <c>if (managedDepartments.Any())</c>, where an empty list applied no filter at all
    /// and a misconfigured role saw the entire estate.
    /// </summary>
    public IReadOnlySet<Guid> Ids { get; }

    /// <summary>
    /// Whether <see cref="Ids"/> should be expanded to include descendants (site trees).
    ///
    /// This is **resolution-time metadata, not a matching rule** — the resolver expands the set
    /// before constructing the constraint, because walking a hierarchy needs a store and matching
    /// must stay pure and translatable to SQL. Carried here so the resolver knows what to expand
    /// and diagnostics can explain what was expanded. Per-dimension rather than per-group, so one
    /// group including child sites can no longer expand a different group's sites (which today's
    /// resolver does, via a single OR'd flag).
    /// </summary>
    public bool IncludeDescendants { get; }

    public bool Matches(ScopeSubject subject) =>
        subject.ValueFor(Dimension) is { } value && Ids.Contains(value);
}

public enum ScopeRuleKind
{
    /// <summary>Grants nothing. The safe default.</summary>
    None = 0,

    /// <summary>Grants only the employee the user is linked to.</summary>
    Self = 1,

    /// <summary>Grants employees matching every constraint (intersection).</summary>
    Constrained = 2,

    /// <summary>Grants every employee. Administrators only.</summary>
    All = 3,
}

/// <summary>
/// What one security group grants. Constraints combine as an <b>intersection</b>: an employee
/// must satisfy every one of them.
/// </summary>
public sealed class ScopeRule
{
    private ScopeRule(ScopeRuleKind kind, IReadOnlyList<ScopeConstraint> constraints)
    {
        Kind = kind;
        Constraints = constraints;
    }

    public ScopeRuleKind Kind { get; }
    public IReadOnlyList<ScopeConstraint> Constraints { get; }

    public static readonly ScopeRule None = new(ScopeRuleKind.None, []);
    public static readonly ScopeRule Self = new(ScopeRuleKind.Self, []);
    public static readonly ScopeRule All = new(ScopeRuleKind.All, []);

    /// <summary>
    /// A rule granting employees who satisfy every constraint.
    ///
    /// Zero constraints collapses to <see cref="None"/>. That matters: intersection over an empty
    /// set is vacuously true, so a "constrained" rule with nothing in it would otherwise grant
    /// everything — the exact fail-open this model exists to remove.
    /// </summary>
    public static ScopeRule Constrained(params ScopeConstraint[] constraints) =>
        constraints.Length == 0 ? None : new ScopeRule(ScopeRuleKind.Constrained, constraints);

    public static ScopeRule Constrained(IEnumerable<ScopeConstraint> constraints) =>
        Constrained(constraints.ToArray());

    public bool Matches(ScopeSubject subject, Guid? selfEmployeeId) => Kind switch
    {
        ScopeRuleKind.All => true,
        ScopeRuleKind.Self => selfEmployeeId is { } self && self == subject.EmployeeId,
        ScopeRuleKind.Constrained => Constraints.All(c => c.Matches(subject)),
        // None, or an enum value from a future version. Fail closed — legacy's equivalent
        // default branch logged and returned the query unfiltered, on purpose.
        _ => false,
    };
}

/// <summary>
/// A user's resolved visibility: the <b>union</b> of what each of their groups grants.
///
/// Union across groups is what makes membership additive — being added to a group can only ever
/// widen what you see. Narrowing is done by removing membership, not by deny-rules, which is the
/// departure from legacy's <c>AccessRightExclusion</c>.
/// </summary>
public sealed class DataScope
{
    private DataScope(IReadOnlyList<ScopeRule> rules, Guid? selfEmployeeId)
    {
        Rules = rules;
        SelfEmployeeId = selfEmployeeId;
    }

    public IReadOnlyList<ScopeRule> Rules { get; }

    /// <summary>The employee this user is linked to, if any. Backs <see cref="ScopeRuleKind.Self"/>.</summary>
    public Guid? SelfEmployeeId { get; }

    public static readonly DataScope Nothing = new([], null);

    public static DataScope Everything() => new([ScopeRule.All], null);

    public static DataScope From(IEnumerable<ScopeRule> rules, Guid? selfEmployeeId = null) =>
        new([.. rules], selfEmployeeId);

    /// <summary>True if any rule grants everything — lets callers skip building a filter.</summary>
    public bool SeesEverything => Rules.Any(r => r.Kind == ScopeRuleKind.All);

    /// <summary>
    /// True if no rule can grant anything. A <see cref="ScopeRuleKind.Self"/> rule without a
    /// linked employee grants nothing, so it does not count as seeing something.
    /// </summary>
    public bool SeesNothing => !Rules.Any(CanGrant);

    private bool CanGrant(ScopeRule rule) => rule.Kind switch
    {
        ScopeRuleKind.None => false,
        ScopeRuleKind.Self => SelfEmployeeId is not null,
        _ => true,
    };

    /// <summary>
    /// Whether a specific employee is visible.
    ///
    /// Kept alongside the query filter (rather than only in it) so access diagnostics can explain
    /// a decision without a second copy of the rule. The two must agree — a divergence between
    /// "what the list returns" and "what diagnostics claims" is the failure legacy's
    /// DataAccessScopeDiagnostics existed to chase.
    /// </summary>
    public bool CanSee(ScopeSubject subject) => Rules.Any(r => r.Matches(subject, SelfEmployeeId));
}
