using WM.Modules.Identity.Domain;
using WM.SharedKernel.Security;

namespace WM.Modules.Identity.Services;

/// <summary>
/// Translates a group's legacy single-kind scope into the composed shape (plan 001).
///
/// Extracted as a pure function on purpose: the data migration and the seeder both use it, and
/// it is the piece that must be provably faithful. A migration that silently widens one group's
/// visibility is the failure mode for this portion, so the mapping is unit-tested per kind rather
/// than trusted to a hand-written UPDATE.
/// </summary>
public static class LegacyScopeMapping
{
    public static (ScopeRuleKind Kind, List<SecurityGroupConstraint> Constraints) FromLegacy(
        DataScopeKind scopeKind,
        bool includeChildSites,
        IEnumerable<Guid> siteIds,
        IEnumerable<Guid> departmentIds)
    {
        switch (scopeKind)
        {
            case DataScopeKind.All:
                return (ScopeRuleKind.All, []);

            case DataScopeKind.Self:
                return (ScopeRuleKind.Self, []);

            case DataScopeKind.Sites:
                // IncludeChildSites was group-wide; it becomes a property of the Site dimension.
                // An empty site list stays empty — under both models that matches nobody, which
                // is the behaviour being preserved, not a bug being introduced.
                return (ScopeRuleKind.Constrained, [
                    Constraint(ScopeDimension.Site, siteIds, includeChildSites)
                ]);

            case DataScopeKind.Departments:
                // Departments are not hierarchical in WM, so descendants never applied here.
                return (ScopeRuleKind.Constrained, [
                    Constraint(ScopeDimension.Department, departmentIds, includeDescendants: false)
                ]);

            // None, or a value written by a newer build. Grant nothing: the migration must never
            // turn a scope it does not understand into a wider one.
            default:
                return (ScopeRuleKind.None, []);
        }
    }

    private static SecurityGroupConstraint Constraint(
        ScopeDimension dimension, IEnumerable<Guid> ids, bool includeDescendants) =>
        new()
        {
            Dimension = dimension,
            IncludeDescendants = includeDescendants,
            Values = [.. ids.Distinct().Select(id => new SecurityGroupConstraintValue
            {
                Dimension = dimension,
                ValueId = id,
            })],
        };

    /// <summary>
    /// The composed rule a persisted group represents. Used by the equivalence tests now and by
    /// the resolver in P3.
    /// </summary>
    public static ScopeRule ToRule(ScopeRuleKind kind, IEnumerable<SecurityGroupConstraint> constraints) =>
        kind switch
        {
            ScopeRuleKind.All => ScopeRule.All,
            ScopeRuleKind.Self => ScopeRule.Self,
            ScopeRuleKind.Constrained => ScopeRule.Constrained(
                constraints.Select(c => new ScopeConstraint(
                    c.Dimension,
                    c.Values.Select(v => v.ValueId),
                    c.IncludeDescendants))),
            _ => ScopeRule.None,
        };
}
