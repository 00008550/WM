using WM.SharedKernel.Security;
using Xunit;

namespace WM.SharedKernel.Tests.Security;

/// <summary>
/// Union semantics across a user's groups. The headline case is D2: today's resolver keeps one
/// Kind and takes the widest, so a user in a site group *and* a department group silently loses
/// the departments they were granted.
/// </summary>
public class DataScopeTests
{
    private static readonly Guid SiteA = Guid.NewGuid();
    private static readonly Guid SiteB = Guid.NewGuid();
    private static readonly Guid DeptX = Guid.NewGuid();
    private static readonly Guid DeptY = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static ScopeSubject Employee(Guid id, Guid site, Guid? dept = null) => new(id, site, dept);

    private static ScopeRule SiteRule(params Guid[] sites) =>
        ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Site, sites));

    private static ScopeRule DeptRule(params Guid[] depts) =>
        ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Department, depts));

    [Fact]
    public void Membership_of_two_groups_on_different_dimensions_grants_both()
    {
        // D2. The old model resolves this to Kind = Sites and discards DeptY entirely, so an
        // employee in DeptY at SiteB becomes invisible despite an explicit grant.
        var scope = DataScope.From([SiteRule(SiteA), DeptRule(DeptY)]);

        Assert.True(scope.CanSee(Employee(Alice, SiteA, DeptX)));   // via the site group
        Assert.True(scope.CanSee(Employee(Bob, SiteB, DeptY)));     // via the department group
        Assert.False(scope.CanSee(Employee(Bob, SiteB, DeptX)));    // neither group grants this
    }

    [Fact]
    public void Groups_union_rather_than_intersect()
    {
        var scope = DataScope.From([SiteRule(SiteA), SiteRule(SiteB)]);

        Assert.True(scope.CanSee(Employee(Alice, SiteA)));
        Assert.True(scope.CanSee(Employee(Bob, SiteB)));
    }

    [Fact]
    public void Intersection_inside_a_group_survives_being_unioned_with_another()
    {
        // One narrow group (dept X at site A) plus one broad group (all of site B).
        var narrow = ScopeRule.Constrained(
            new ScopeConstraint(ScopeDimension.Department, [DeptX]),
            new ScopeConstraint(ScopeDimension.Site, [SiteA]));

        var scope = DataScope.From([narrow, SiteRule(SiteB)]);

        Assert.True(scope.CanSee(Employee(Alice, SiteA, DeptX)));   // matches the narrow group
        Assert.False(scope.CanSee(Employee(Alice, SiteA, DeptY)));  // narrow group still intersects
        Assert.True(scope.CanSee(Employee(Bob, SiteB, DeptY)));     // matches the broad group
    }

    [Fact]
    public void No_groups_grants_nothing()
    {
        Assert.True(DataScope.Nothing.SeesNothing);
        Assert.False(DataScope.Nothing.SeesEverything);
        Assert.False(DataScope.Nothing.CanSee(Employee(Alice, SiteA, DeptX)));
    }

    [Fact]
    public void One_unrestricted_group_grants_everything()
    {
        var scope = DataScope.From([DeptRule(DeptX), ScopeRule.All]);

        Assert.True(scope.SeesEverything);
        Assert.True(scope.CanSee(Employee(Bob, SiteB, DeptY)));
    }

    [Fact]
    public void An_employee_linked_user_always_sees_themselves()
    {
        // Preserves today's behaviour: self-service must not depend on an admin remembering
        // to add the user to a group.
        var scope = DataScope.From([ScopeRule.Self], selfEmployeeId: Alice);

        Assert.True(scope.CanSee(Employee(Alice, SiteA, DeptX)));
        Assert.False(scope.CanSee(Employee(Bob, SiteA, DeptX)));
        Assert.False(scope.SeesNothing);
    }

    [Fact]
    public void A_self_rule_without_a_linked_employee_sees_nothing()
    {
        var scope = DataScope.From([ScopeRule.Self], selfEmployeeId: null);

        Assert.True(scope.SeesNothing);
        Assert.False(scope.CanSee(Employee(Alice, SiteA, DeptX)));
    }

    [Fact]
    public void A_group_that_grants_nothing_does_not_widen_the_union()
    {
        var scope = DataScope.From([ScopeRule.None, DeptRule(DeptX)]);

        Assert.False(scope.SeesNothing);
        Assert.True(scope.CanSee(Employee(Alice, SiteA, DeptX)));
        Assert.False(scope.CanSee(Employee(Bob, SiteB, DeptY)));
    }

    [Fact]
    public void Descendant_expansion_is_resolution_metadata_not_a_matching_rule()
    {
        // IncludeDescendants tells the resolver what to expand; it must not make matching
        // permissive on its own. A child site id that was never expanded into the set does
        // not match. Per-dimension, so one group's expansion cannot widen another's sites.
        var childSite = Guid.NewGuid();
        var rule = ScopeRule.Constrained(
            new ScopeConstraint(ScopeDimension.Site, [SiteA], includeDescendants: true));

        var scope = DataScope.From([rule]);

        Assert.True(scope.CanSee(Employee(Alice, SiteA)));
        Assert.False(scope.CanSee(Employee(Bob, childSite)));
    }
}
