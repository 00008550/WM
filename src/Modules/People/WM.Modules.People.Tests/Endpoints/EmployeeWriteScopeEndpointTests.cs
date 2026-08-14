using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// 003 P2b at the transport: what <c>POST /api/employees</c> and <c>PUT /api/employees/{id}</c>
/// actually answer. These are the tests that would have caught PHASE-AUDIT.md A2's second half —
/// create was not scope-checked at all, and update checked only the record it started from, so a
/// manager could move someone out of their own scope and lose sight of them.
///
/// <para>
/// The status codes are the assertion, and the split between them is deliberate: a refused
/// destination is <b>403</b> because the caller supplied it, while a record the caller cannot see
/// stays <b>404</b> because its existence is the secret. A test that accepted either would let the
/// pre-image check start disclosing who works here.
/// </para>
/// </summary>
public sealed class EmployeeWriteScopeEndpointTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeptA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeptB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid AtSiteA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AtSiteB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly DateOnly Hired = new(2024, 1, 15);

    private const string NewCode = "E2001";

    /// <summary>The seeded site-B employee's code — a badge number already taken, out of scope.</summary>
    private const string TakenCode = "E1002";

    [Fact]
    public async Task A_create_at_a_site_outside_the_callers_scope_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", NewEmployee(SiteB));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("outside your data scope", await response.Content.ReadAsStringAsync());
        // A 403 that inserts anyway is the same defect wearing a different status code.
        Assert.Empty(host.Read(db => db.Employees.AsNoTracking().Where(e => e.Code == NewCode).ToList()));
    }

    [Fact]
    public async Task A_create_inside_the_callers_scope_still_succeeds()
    {
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", NewEmployee(SiteA, DeptA));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Code == NewCode));
        Assert.Equal(SiteA, created.SiteId);
    }

    [Fact]
    public async Task A_caller_whose_scope_grants_nothing_creates_nobody()
    {
        // The fail-closed arm, reached through the transport rather than argued about: holding
        // employees.manage and no scope is a real configuration (a user in no security group).
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.None, Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", NewEmployee(SiteA, DeptA));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Read(db => db.Employees.AsNoTracking().Where(e => e.Code == NewCode).ToList()));
    }

    [Fact]
    public async Task A_create_outside_the_callers_scope_is_refused_before_the_code_is_probed()
    {
        // Ordering, not a status code — the POST equivalent of the 404-before-403 test below. The
        // code posted here is the one already worn by the seeded employee at site B, whom this
        // caller cannot see, and the site is site B as well. Both refusals are available: the scope
        // check answers 403, the uniqueness probe would answer 409. Which one arrives says which
        // ran first, and 409 is a yes/no oracle on badge numbers.
        //
        // What this does NOT establish is that the oracle is closed. The uniqueness probe is
        // unscoped, so a caller holding employees.manage can still ask about any badge number in
        // the estate by naming a site they can write to — the probe answers estate-wide either way.
        // Scoping the probe belongs to plan 007; this test pins only that a caller with nowhere
        // in scope to name cannot reach it at all.
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", NewEmployee(SiteB, DeptB, TakenCode));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("outside your data scope", body);
        Assert.DoesNotContain("already exists", body);
    }

    [Fact]
    public async Task An_edit_that_moves_the_employee_out_of_the_callers_scope_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{AtSiteA}", Rename(SiteB, DeptB));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("outside your data scope", await response.Content.ReadAsStringAsync());
        // Nothing at all was saved — not the site, and not the fields that came with it.
        var unchanged = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteA));
        Assert.Equal(SiteA, unchanged.SiteId);
        Assert.Equal("North", unchanged.LastName);
    }

    [Fact]
    public async Task An_edit_within_the_callers_scope_still_succeeds()
    {
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{AtSiteA}", Rename(SiteA, DeptA));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteA));
        Assert.Equal("Lovelace", saved.LastName);
        Assert.Equal(SiteA, saved.SiteId);
    }

    [Fact]
    public async Task An_employee_the_caller_cannot_see_is_still_404_rather_than_403()
    {
        // Ordering, not just a status code: the destination here is also out of scope, so if the
        // post-image check ran first this would answer 403 and confirm the record exists.
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{AtSiteB}", Rename(SiteB, DeptB));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteB));
        Assert.Equal("South", unchanged.LastName);
    }

    [Fact]
    public async Task An_unrestricted_caller_may_create_anywhere_and_move_anyone()
    {
        // The portion must not narrow an administrator. All is the one arm with no dimension in it.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var created = await client.PostAsJsonAsync("/api/employees", NewEmployee(SiteB, DeptB));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var moved = await client.PutAsJsonAsync($"/api/employees/{AtSiteA}", Rename(SiteB, DeptB));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(SiteB, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteA)).SiteId);
    }

    [Fact]
    public async Task A_department_scoped_caller_may_not_clear_the_department_that_makes_the_record_visible()
    {
        // The same rule on the other dimension. No department is not a wildcard — WithinScope says
        // an employee with none is invisible to a department-scoped user — so blanking it is an
        // escape, and the SPA sends departmentId: null on every edit today (plan 003 P3, B5).
        await using var host = await PeopleEndpointHost.StartAsync(Departments(DeptA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{AtSiteA}", Rename(SiteA, departmentId: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DeptA, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteA)).DepartmentId);
    }

    private static EffectiveDataScope Sites(params Guid[] siteIds) =>
        new(DataScopeKind.Sites, new HashSet<Guid>(siteIds), new HashSet<Guid>(), null);

    private static EffectiveDataScope Departments(params Guid[] departmentIds) =>
        new(DataScopeKind.Departments, new HashSet<Guid>(), new HashSet<Guid>(departmentIds), null);

    private static EmployeeUpsertRequest NewEmployee(
        Guid siteId, Guid? departmentId = null, string code = NewCode) =>
        new(code, "Grace", "Hopper", null, null, null, siteId, departmentId, Hired);

    /// <summary>A full-replace PUT body for the seeded site-A employee, with a new surname.</summary>
    private static EmployeeUpsertRequest Rename(Guid siteId, Guid? departmentId) =>
        new("E1001", "Ada", "Lovelace", null, null, null, siteId, departmentId, Hired);

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = SiteA, Name = "North" });
        db.Sites.Add(new Site { Id = SiteB, Name = "South" });
        db.Employees.Add(new Employee
        {
            Id = AtSiteA,
            Code = "E1001",
            FirstName = "Ada",
            LastName = "North",
            SiteId = SiteA,
            DepartmentId = DeptA,
            HireDate = Hired,
        });
        db.Employees.Add(new Employee
        {
            Id = AtSiteB,
            Code = "E1002",
            FirstName = "Bob",
            LastName = "South",
            SiteId = SiteB,
            DepartmentId = DeptB,
            HireDate = Hired,
        });
    }
}
