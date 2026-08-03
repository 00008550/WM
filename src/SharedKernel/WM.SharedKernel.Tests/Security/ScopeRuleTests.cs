using WM.SharedKernel.Security;
using Xunit;

namespace WM.SharedKernel.Tests.Security;

/// <summary>
/// Intersection semantics within a single rule, and the fail-closed behaviours that are the
/// point of the model. Each legacy fail-open documented in ARCHITECTURE.md §14 decision 5 has a
/// test here that names it.
/// </summary>
public class ScopeRuleTests
{
    private static readonly Guid SiteA = Guid.NewGuid();
    private static readonly Guid SiteB = Guid.NewGuid();
    private static readonly Guid DeptX = Guid.NewGuid();
    private static readonly Guid DeptY = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static ScopeSubject At(Guid site, Guid? dept = null, Guid? building = null, Guid? id = null) =>
        new(id ?? Alice, site, dept, building);

    [Fact]
    public void Constraints_within_a_rule_intersect()
    {
        // "departments X, but only at site A" — the configuration the single-enum model
        // could not express at all.
        var rule = ScopeRule.Constrained(
            new ScopeConstraint(ScopeDimension.Department, [DeptX]),
            new ScopeConstraint(ScopeDimension.Site, [SiteA]));

        Assert.True(rule.Matches(At(SiteA, DeptX), null));
        Assert.False(rule.Matches(At(SiteB, DeptX), null));
        Assert.False(rule.Matches(At(SiteA, DeptY), null));
    }

    [Fact]
    public void A_constraint_with_no_ids_matches_nobody()
    {
        // Legacy fail-open #1: `if (managedDepartments.Any())` applied no filter when the list
        // was empty, so a misconfigured role saw the entire estate.
        var rule = ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Department, []));

        Assert.False(rule.Matches(At(SiteA, DeptX), null));
        Assert.False(rule.Matches(At(SiteA, null), null));
    }

    [Fact]
    public void A_null_value_on_the_employee_never_satisfies_a_constraint()
    {
        // Legacy fail-open #2: `EmployeeLocationId == null || managedLocations.Contains(...)`
        // made every location-less employee visible to every role.
        var rule = ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Department, [DeptX]));

        Assert.False(rule.Matches(At(SiteA, dept: null), null));
    }

    [Fact]
    public void A_constrained_rule_with_no_constraints_grants_nothing()
    {
        // Intersection over an empty set is vacuously true, so this would otherwise grant
        // everything. The factory collapses it to None instead.
        var rule = ScopeRule.Constrained();

        Assert.Equal(ScopeRuleKind.None, rule.Kind);
        Assert.False(rule.Matches(At(SiteA, DeptX), null));
    }

    [Fact]
    public void Reserved_dimensions_match_nobody_until_their_entities_exist()
    {
        // CostCentre and WorkActivity are declared so the shape survives phases 2 and 7.
        // Until then a group constrained on one must grant nothing, not everything.
        var rule = ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.CostCentre, [Guid.NewGuid()]));

        Assert.False(rule.Matches(At(SiteA, DeptX), null));
    }

    [Fact]
    public void A_constraint_does_not_alias_the_caller_s_id_set()
    {
        // A HashSet<Guid> satisfies IReadOnlySet<Guid>, so a non-copying constructor would leave
        // the caller holding a mutable handle on a live security constraint. The resolver builds
        // its id sets incrementally, so this is a reachable widening, not a theoretical one.
        var ids = new HashSet<Guid> { DeptX };
        var rule = ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Department, ids));

        ids.Add(DeptY);

        Assert.True(rule.Matches(At(SiteA, DeptX), null));
        Assert.False(rule.Matches(At(SiteA, DeptY), null));
    }

    [Fact]
    public void All_grants_every_employee()
    {
        Assert.True(ScopeRule.All.Matches(At(SiteA, DeptX), null));
        Assert.True(ScopeRule.All.Matches(At(SiteB, null), null));
    }

    [Fact]
    public void None_grants_nothing()
    {
        Assert.False(ScopeRule.None.Matches(At(SiteA, DeptX), null));
    }

    [Fact]
    public void Self_grants_only_the_linked_employee()
    {
        Assert.True(ScopeRule.Self.Matches(At(SiteA, id: Alice), Alice));
        Assert.False(ScopeRule.Self.Matches(At(SiteA, id: Bob), Alice));
    }

    [Fact]
    public void Self_grants_nothing_when_the_user_is_not_linked_to_an_employee()
    {
        Assert.False(ScopeRule.Self.Matches(At(SiteA, id: Alice), null));
    }

    [Fact]
    public void An_explicit_employee_list_scopes_to_those_employees()
    {
        // Legacy's ByEmployees management type, which the old model had no equivalent of.
        var rule = ScopeRule.Constrained(new ScopeConstraint(ScopeDimension.Employee, [Alice]));

        Assert.True(rule.Matches(At(SiteA, id: Alice), null));
        Assert.False(rule.Matches(At(SiteA, id: Bob), null));
    }

    [Fact]
    public void An_unknown_dimension_grants_nothing()
    {
        // Legacy fail-open #3: its default branch logged Fatal and returned the query
        // unfiltered, commented as intentional. A dimension this build does not understand —
        // a group persisted by a newer version, say — must grant nothing instead.
        var rule = ScopeRule.Constrained(new ScopeConstraint((ScopeDimension)999, [SiteA]));

        Assert.False(rule.Matches(At(SiteA, DeptX), null));
    }

    [Fact]
    public void An_unknown_rule_kind_grants_nothing()
    {
        // The same guarantee one level up, for the rule's own kind.
        var scope = DataScope.From([ScopeRule.None]);

        Assert.True(scope.SeesNothing);
        Assert.False(scope.CanSee(At(SiteA, DeptX)));
    }
}
