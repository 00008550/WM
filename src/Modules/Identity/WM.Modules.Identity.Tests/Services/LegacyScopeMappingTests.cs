using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.Identity.Tests.Services;

/// <summary>
/// The migration's correctness proof.
///
/// Plan 001 P2's acceptance bar is that every existing group means **exactly** what it meant
/// before. So these tests do not check the mapping's shape — they check that the composed rule
/// and the legacy <see cref="EffectiveDataScope"/> agree on which employees are visible, for
/// every legacy kind and for the awkward inputs (empty lists, null discriminators).
///
/// Per-group equivalence only. The union-across-groups defect (D2) is deliberately still present
/// after this portion and is fixed in P3.
/// </summary>
public class LegacyScopeMappingTests
{
    private static readonly Guid SiteA = Guid.NewGuid();
    private static readonly Guid SiteB = Guid.NewGuid();
    private static readonly Guid DeptX = Guid.NewGuid();
    private static readonly Guid DeptY = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    /// <summary>Every employee shape the two models must agree about.</summary>
    public static readonly (Guid Id, Guid Site, Guid? Dept)[] Population =
    [
        (Alice, SiteA, DeptX),
        (Bob,   SiteA, DeptY),
        (Bob,   SiteB, DeptX),
        (Alice, SiteB, null),   // no department — the fail-open legacy allowed elsewhere
        (Bob,   SiteA, null),
    ];

    private static void AssertAgrees(
        DataScopeKind kind, bool includeChildSites, Guid[] sites, Guid[] depts, Guid? self = null)
    {
        var legacy = new EffectiveDataScope(kind, sites.ToHashSet(), depts.ToHashSet(), self);

        var (ruleKind, constraints) = LegacyScopeMapping.FromLegacy(kind, includeChildSites, sites, depts);
        var composed = DataScope.From([LegacyScopeMapping.ToRule(ruleKind, constraints)], self);

        foreach (var (id, site, dept) in Population)
        {
            var before = legacy.CanSee(id, site, dept);
            var after = composed.CanSee(new ScopeSubject(id, site, dept));

            Assert.True(before == after,
                $"{kind} disagreed for employee at site/dept ({site}/{dept}): legacy={before}, composed={after}");
        }
    }

    [Fact]
    public void All_is_preserved()
    {
        AssertAgrees(DataScopeKind.All, includeChildSites: true, [], []);
    }

    [Fact]
    public void Sites_is_preserved()
    {
        AssertAgrees(DataScopeKind.Sites, includeChildSites: true, [SiteA], []);
        AssertAgrees(DataScopeKind.Sites, includeChildSites: false, [SiteA, SiteB], []);
    }

    [Fact]
    public void Departments_is_preserved()
    {
        AssertAgrees(DataScopeKind.Departments, includeChildSites: true, [], [DeptX]);
        AssertAgrees(DataScopeKind.Departments, includeChildSites: false, [], [DeptX, DeptY]);
    }

    [Fact]
    public void Self_is_preserved()
    {
        AssertAgrees(DataScopeKind.Self, includeChildSites: true, [], [], self: Alice);
    }

    [Fact]
    public void None_is_preserved()
    {
        AssertAgrees(DataScopeKind.None, includeChildSites: true, [], []);
    }

    [Fact]
    public void An_empty_site_list_still_matches_nobody()
    {
        // The migration must not turn "Sites, but none selected" into "everything".
        AssertAgrees(DataScopeKind.Sites, includeChildSites: true, [], []);

        var (kind, constraints) = LegacyScopeMapping.FromLegacy(DataScopeKind.Sites, true, [], []);
        var scope = DataScope.From([LegacyScopeMapping.ToRule(kind, constraints)]);
        Assert.False(scope.CanSee(new ScopeSubject(Alice, SiteA, DeptX)));
    }

    [Fact]
    public void An_empty_department_list_still_matches_nobody()
    {
        AssertAgrees(DataScopeKind.Departments, includeChildSites: false, [], []);
    }

    [Fact]
    public void IncludeChildSites_lands_on_the_site_dimension()
    {
        // Was group-wide; now a property of the Site constraint, so one group can no longer
        // expand another group's sites.
        var (_, constraints) = LegacyScopeMapping.FromLegacy(DataScopeKind.Sites, true, [SiteA], []);
        var site = Assert.Single(constraints);
        Assert.Equal(ScopeDimension.Site, site.Dimension);
        Assert.True(site.IncludeDescendants);

        var (_, off) = LegacyScopeMapping.FromLegacy(DataScopeKind.Sites, false, [SiteA], []);
        Assert.False(Assert.Single(off).IncludeDescendants);
    }

    [Fact]
    public void Department_groups_never_inherit_descendant_expansion()
    {
        // IncludeChildSites was group-wide, so a department-scoped group could carry it set.
        // It must not become descendant expansion on the Department dimension.
        var (_, constraints) = LegacyScopeMapping.FromLegacy(DataScopeKind.Departments, true, [], [DeptX]);
        var dept = Assert.Single(constraints);
        Assert.Equal(ScopeDimension.Department, dept.Dimension);
        Assert.False(dept.IncludeDescendants);
    }

    [Fact]
    public void An_unrecognised_scope_kind_grants_nothing()
    {
        // A row written by a newer build must never migrate into something wider.
        var (kind, constraints) = LegacyScopeMapping.FromLegacy((DataScopeKind)99, true, [SiteA], [DeptX]);

        Assert.Equal(ScopeRuleKind.None, kind);
        Assert.Empty(constraints);

        var scope = DataScope.From([LegacyScopeMapping.ToRule(kind, constraints)]);
        Assert.True(scope.SeesNothing);
    }

    [Fact]
    public void Duplicate_ids_collapse()
    {
        var (_, constraints) = LegacyScopeMapping.FromLegacy(
            DataScopeKind.Sites, false, [SiteA, SiteA, SiteB], []);

        Assert.Equal(2, Assert.Single(constraints).Values.Count);
    }
}
