using WM.Api.Realtime;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Realtime;

/// <summary>
/// The realtime rule must be the same rule as the query rule. These tests pin the equivalence
/// directly: a connection hears an event exactly when <see cref="EffectiveDataScope.CanSee"/>
/// says its user may see that employee.
/// </summary>
public sealed class AttendanceScopeGroupTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeptA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeptB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Alice = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Bob = Guid.Parse("66666666-6666-6666-6666-666666666666");

    internal static EffectiveDataScope Sites(params Guid[] siteIds) =>
        new(DataScopeKind.Sites, new HashSet<Guid>(siteIds), new HashSet<Guid>(), null);

    internal static EffectiveDataScope Departments(params Guid[] departmentIds) =>
        new(DataScopeKind.Departments, new HashSet<Guid>(), new HashSet<Guid>(departmentIds), null);

    internal static EffectiveDataScope Self(Guid? employeeId) =>
        new(DataScopeKind.Self, new HashSet<Guid>(), new HashSet<Guid>(), employeeId);

    private sealed record Case(
        string Description, EffectiveDataScope Scope, Guid EmployeeId, Guid SiteId, Guid? DepartmentId);

    // One table, used by both properties below. A single [Fact] that walks it rather than a
    // [Theory]: EffectiveDataScope is not xUnit-serializable, and a loop with a message on the
    // assertion reports the failing row just as clearly.
    private static readonly Case[] Cases =
    [
        new("administrator",                  EffectiveDataScope.All(), Alice, SiteA, DeptA),
        new("administrator, no department",   EffectiveDataScope.All(), Alice, SiteA, null),
        new("manager at the punched site",    Sites(SiteA),             Alice, SiteA, DeptA),
        new("manager at another site",        Sites(SiteB),             Alice, SiteA, DeptA),
        new("manager over several sites",     Sites(SiteA, SiteB),      Alice, SiteB, DeptA),
        new("department manager, matching",   Departments(DeptA),       Alice, SiteA, DeptA),
        new("department manager, other dept", Departments(DeptB),       Alice, SiteA, DeptA),
        new("department manager, no dept",    Departments(DeptA),       Alice, SiteA, null),
        new("self-service, own punch",        Self(Alice),              Alice, SiteA, DeptA),
        new("self-service, someone else's",   Self(Alice),              Bob,   SiteA, DeptA),
        new("self-service, unlinked account", Self(null),               Alice, SiteA, DeptA),
        new("no scope at all",                EffectiveDataScope.None,  Alice, SiteA, DeptA),
    ];

    private static IEnumerable<string> Matched(Case c) =>
        AttendanceScopeGroups.ForScope(c.Scope)
            .Intersect(AttendanceScopeGroups.ForSubject(c.EmployeeId, c.SiteId, c.DepartmentId));

    [Fact]
    public void Group_membership_agrees_with_the_query_scope_rule()
    {
        foreach (var c in Cases)
        {
            var canSee = c.Scope.CanSee(c.EmployeeId, c.SiteId, c.DepartmentId);
            Assert.True(
                canSee == Matched(c).Any(),
                $"{c.Description}: the query rule says {canSee}, the realtime rule says {!canSee}.");
        }
    }

    [Fact]
    public void A_connection_never_receives_the_same_event_twice()
    {
        // SignalR's default lifetime manager does not de-duplicate across the groups a message is
        // addressed to, so a connection in two addressed groups gets two copies. Today's resolver
        // gives a scope a single Kind, so a connection's groups all sit on one axis and a punch
        // can match at most one of them. Plan 001 P3 unions rules across groups — when it lands,
        // this assertion is the tripwire that says "decide how the hub de-duplicates".
        foreach (var c in Cases)
            Assert.True(
                Matched(c).Count() <= 1,
                $"{c.Description}: more than one group matched, so the client would render a duplicate.");
    }

    [Fact]
    public void A_scope_that_grants_nothing_joins_no_group()
    {
        Assert.Empty(AttendanceScopeGroups.ForScope(EffectiveDataScope.None));
        Assert.Empty(AttendanceScopeGroups.ForScope(Self(null)));
        // An empty site list is "nobody", not "everybody" — the fail-closed inversion of legacy.
        Assert.Empty(AttendanceScopeGroups.ForScope(Sites()));
        Assert.Empty(AttendanceScopeGroups.ForScope(Departments()));
    }

    [Fact]
    public void An_administrator_joins_one_group_however_large_the_estate()
    {
        // The fan-out has to stay O(groups). If "All" ever expanded to a group per site, every
        // punch would address a list that grows with the customer.
        Assert.Equal(new[] { AttendanceScopeGroups.Everyone },
            AttendanceScopeGroups.ForScope(EffectiveDataScope.All()));
    }

    [Fact]
    public void An_event_is_addressed_to_one_group_per_axis_the_employee_sits_on()
    {
        Assert.Equal(
            new[]
            {
                AttendanceScopeGroups.Everyone, AttendanceScopeGroups.Site(SiteA),
                AttendanceScopeGroups.Self(Alice), AttendanceScopeGroups.Department(DeptA),
            },
            AttendanceScopeGroups.ForSubject(Alice, SiteA, DeptA));

        Assert.Equal(
            new[]
            {
                AttendanceScopeGroups.Everyone, AttendanceScopeGroups.Site(SiteA),
                AttendanceScopeGroups.Self(Alice),
            },
            AttendanceScopeGroups.ForSubject(Alice, SiteA, null));
    }

    [Fact]
    public void Group_names_from_different_axes_can_never_collide()
    {
        // The same guid used as a site, a department and an employee must produce three distinct
        // groups — otherwise a department-scoped user could inherit a site's audience.
        var id = SiteA;
        Assert.Equal(3, new HashSet<string>
        {
            AttendanceScopeGroups.Site(id),
            AttendanceScopeGroups.Department(id),
            AttendanceScopeGroups.Self(id),
        }.Count);
    }
}
