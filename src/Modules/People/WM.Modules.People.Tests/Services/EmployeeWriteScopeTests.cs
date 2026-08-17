using WM.Modules.People.Domain;
using WM.Modules.People.Services;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Services;

/// <summary>
/// The write predicate itself: one rule, every scope shape, and the pin that keeps it honest.
///
/// <para>
/// The load-bearing test here is <see cref="The_write_predicate_and_the_read_filter_answer_the_same_question"/>.
/// A predicate beside a query filter is how legacy ended up with five copies of its employee filter
/// (<c>TLW-AUTHORIZATION-MODEL.md</c> §10), and a divergence between them is silent — the list keeps
/// showing what the list showed while the write path quietly permits something else. So the two are
/// asserted against each other rather than reviewed against each other.
/// </para>
/// </summary>
public sealed class EmployeeWriteScopeTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeptA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeptB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Unfiled = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    [Fact]
    public void A_site_scoped_caller_may_only_write_inside_their_sites()
    {
        var scope = Sites(SiteA);

        Assert.True(scope.PermitsWrite(Ada, SiteA, DeptB));
        Assert.False(scope.PermitsWrite(Ada, SiteB, DeptA));
    }

    [Fact]
    public void A_department_scoped_caller_is_refused_a_record_with_no_department()
    {
        // Consistent with WithinScope: a null department satisfies no department constraint, so it
        // is an escape rather than a wildcard.
        var scope = Departments(DeptA);

        Assert.True(scope.PermitsWrite(Ada, SiteB, DeptA));
        Assert.False(scope.PermitsWrite(Ada, SiteA, departmentId: null));
    }

    [Fact]
    public void A_create_has_no_id_and_so_is_granted_only_by_a_dimension()
    {
        // The two arms that answer by id cannot grant a create: there is no record yet.
        Assert.True(Sites(SiteA).PermitsWrite(employeeId: null, SiteA, DeptA));
        Assert.False(Self(Ada).PermitsWrite(employeeId: null, SiteA, DeptA));
        Assert.False(EffectiveDataScope.None.PermitsWrite(employeeId: null, SiteA, DeptA));
    }

    [Fact]
    public void An_unrestricted_scope_permits_every_image()
    {
        var scope = EffectiveDataScope.All();

        Assert.True(scope.PermitsWrite(Ada, SiteB, DeptB));
        Assert.True(scope.PermitsWrite(employeeId: null, SiteB, departmentId: null));
    }

    [Fact]
    public void A_scope_of_an_unknown_kind_permits_nothing()
    {
        // Fail closed on a value from a future version, matching WithinScope's own default arm.
        // The old model is a persisted enum (SecurityGroup.ScopeKind), so this is reachable by a
        // database that has been rolled forward and back rather than only in theory.
        var scope = new EffectiveDataScope(
            (DataScopeKind)99, new HashSet<Guid> { SiteA }, new HashSet<Guid> { DeptA }, Ada);

        Assert.False(scope.PermitsWrite(Ada, SiteA, DeptA));
        Assert.False(scope.PermitsWrite(employeeId: null, SiteA, DeptA));
    }

    [Fact]
    public void Editing_your_own_record_is_permitted_today_and_that_is_the_seam()
    {
        // Legacy's Role.CanModifySelf says a group may be configured to allow seeing your own record
        // without editing it (AuthorizationService.cs:281-286). WM adopted the flag on 2026-08-05
        // and no group carries it yet, so the predicate answers true and the write is allowed.
        //
        // This is pinned rather than left implicit so that 005 P4 — which wires
        // CanEditOwnRecord to the real group flag — has to come back here and say what changed.
        var scope = Self(Ada);

        Assert.True(scope.CanEditOwnRecord());
        Assert.True(scope.PermitsWrite(Ada, SiteA, DeptA));
        Assert.False(scope.PermitsWrite(Bob, SiteA, DeptA));
    }

    [Fact]
    public void The_write_predicate_and_the_read_filter_answer_the_same_question()
    {
        List<string> mismatches = [];

        foreach (var scope in EveryScopeShape())
        {
            var visible = Everyone.AsQueryable().WithinScope(scope).Select(e => e.Id).Order().ToArray();
            var writable = Everyone
                .Where(e => scope.PermitsWrite(e.Id, e.SiteId, e.DepartmentId))
                .Select(e => e.Id)
                .Order()
                .ToArray();

            // The invariant that must survive CanEditOwnRecord being wired to a real flag: the
            // write set can only ever shrink relative to the read set, never exceed it.
            foreach (var escaped in writable.Except(visible))
                mismatches.Add($"{Describe(scope)}: writable but not visible — {escaped}");

            // And today the two are exactly equal, because nothing narrows writes yet. When 005 P4
            // makes a group refuse self-edits, this is the line that fails — deliberately.
            if (!visible.SequenceEqual(writable))
                mismatches.Add($"{Describe(scope)}: read {Names(visible)} vs write {Names(writable)}");
        }

        Assert.Equal([], mismatches);
    }

    private static string Describe(EffectiveDataScope scope) =>
        $"{scope.Kind} sites=[{string.Join(',', scope.SiteIds)}] "
        + $"depts=[{string.Join(',', scope.DepartmentIds)}] self={scope.SelfEmployeeId}";

    private static string Names(IEnumerable<Guid> ids) => $"[{string.Join(',', ids)}]";

    private static IEnumerable<EffectiveDataScope> EveryScopeShape() =>
    [
        EffectiveDataScope.All(),
        EffectiveDataScope.None,
        Sites(SiteA),
        Sites(SiteA, SiteB),
        Sites(),
        Departments(DeptA),
        Departments(),
        Self(Ada),
        Self(Unfiled),
        new EffectiveDataScope(DataScopeKind.Self, new HashSet<Guid>(), new HashSet<Guid>(), null),
        new EffectiveDataScope((DataScopeKind)99, new HashSet<Guid> { SiteA }, new HashSet<Guid> { DeptA }, Ada),
    ];

    private static readonly List<Employee> Everyone =
    [
        Employee(Ada, SiteA, DeptA),
        Employee(Bob, SiteB, DeptB),
        // No department: the arm that most often disagrees between a filter and a predicate.
        Employee(Unfiled, SiteA, departmentId: null),
    ];

    private static Employee Employee(Guid id, Guid siteId, Guid? departmentId) => new()
    {
        Id = id,
        Code = $"E{id.ToString()[..4]}",
        FirstName = "Test",
        LastName = "Employee",
        SiteId = siteId,
        DepartmentId = departmentId,
        EmployedFrom = new DateOnly(2024, 1, 15),
    };

    private static EffectiveDataScope Sites(params Guid[] siteIds) =>
        new(DataScopeKind.Sites, new HashSet<Guid>(siteIds), new HashSet<Guid>(), null);

    private static EffectiveDataScope Departments(params Guid[] departmentIds) =>
        new(DataScopeKind.Departments, new HashSet<Guid>(), new HashSet<Guid>(departmentIds), null);

    private static EffectiveDataScope Self(Guid employeeId) =>
        new(DataScopeKind.Self, new HashSet<Guid>(), new HashSet<Guid>(), employeeId);
}
