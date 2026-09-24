using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// 003 P3 at the transport: <c>GET /api/sites</c> and <c>GET /api/departments</c> answer only with
/// the places the caller's scope reaches, and an edit made from the row the list returns keeps the
/// department and phone it started with.
/// </summary>
public sealed class OrganisationScopeEndpointTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeptA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeptA2 = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DeptB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid AtSiteA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AtSiteB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private const string AdaPhone = "+44 20 7946 0001";

    [Fact]
    public async Task An_unrestricted_caller_sees_every_site()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal([SiteA, SiteB], await IdsAsync(client, "/api/sites"));
    }

    [Fact]
    public async Task A_site_scoped_caller_sees_only_their_sites()
    {
        await using var host = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal([SiteA], await IdsAsync(client, "/api/sites"));
    }

    [Fact]
    public async Task A_department_scoped_caller_sees_the_sites_their_departments_sit_at()
    {
        // Without the site, the editor's site field has nothing to offer them and every save is a 400.
        await using var host = await PeopleEndpointHost.StartAsync(Departments(DeptB), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal([SiteB], await IdsAsync(client, "/api/sites"));
    }

    [Fact]
    public async Task A_self_scoped_caller_sees_only_their_own_site_and_department()
    {
        var self = new EffectiveDataScope(DataScopeKind.Self, new HashSet<Guid>(), new HashSet<Guid>(), AtSiteB);
        await using var host = await PeopleEndpointHost.StartAsync(self, Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal([SiteB], await IdsAsync(client, "/api/sites"));
        Assert.Equal([DeptB], await IdsAsync(client, "/api/departments"));
    }

    [Fact]
    public async Task A_caller_whose_scope_grants_nothing_sees_no_sites_and_no_departments()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.None, Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Empty(await IdsAsync(client, "/api/sites"));
        Assert.Empty(await IdsAsync(client, "/api/departments"));
    }

    [Fact]
    public async Task Departments_are_scoped_like_sites()
    {
        await using var all = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        Assert.Equal(
            new HashSet<Guid> { DeptA, DeptA2, DeptB },
            (await IdsAsync(all.ClientWith(WmPermissions.EmployeesView), "/api/departments")).ToHashSet());

        await using var siteScoped = await PeopleEndpointHost.StartAsync(Sites(SiteA), Seed);
        Assert.Equal(
            new HashSet<Guid> { DeptA, DeptA2 },
            (await IdsAsync(siteScoped.ClientWith(WmPermissions.EmployeesView), "/api/departments")).ToHashSet());

        await using var deptScoped = await PeopleEndpointHost.StartAsync(Departments(DeptA), Seed);
        Assert.Equal([DeptA], await IdsAsync(deptScoped.ClientWith(WmPermissions.EmployeesView), "/api/departments"));
    }

    [Fact]
    public async Task Departments_can_be_narrowed_to_one_site()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal([DeptB], await IdsAsync(client, $"/api/departments?siteId={SiteB}"));
    }

    [Fact]
    public async Task The_department_list_requires_employees_view()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.SelfService);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/departments")).StatusCode);
    }

    [Fact]
    public async Task A_department_scoped_edit_made_from_the_list_row_keeps_department_and_phone()
    {
        // The round-trip the SPA performs, run as the caller for whom it used to break: read the row
        // from the list, change one field, send the row back as a full replace. Before P3 the list
        // carried no phone and the editor sent departmentId: null — a department-scoped caller got
        // 403 (P2b), and anyone else silently lost both. Run as Departments, never All: an
        // unrestricted caller short-circuits the scope check and would pass over the defect.
        await using var host = await PeopleEndpointHost.StartAsync(Departments(DeptA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView, WmPermissions.EmployeesManage);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/employees");
        var row = list.GetProperty("items").EnumerateArray().Single();

        var edit = new EmployeeUpsertRequest(
            row.GetProperty("code").GetString()!,
            row.GetProperty("firstName").GetString()!,
            row.GetProperty("lastName").GetString()!,
            Str(row, "email"),
            Str(row, "phone"),
            "Shift supervisor",
            row.GetProperty("siteId").GetGuid(),
            row.GetProperty("departmentId").ValueKind == JsonValueKind.Null ? null : row.GetProperty("departmentId").GetGuid(),
            DateOnly.Parse(row.GetProperty("employedFrom").GetString()!),
            Version: row.GetProperty("version").GetGuid());

        var response = await client.PutAsJsonAsync($"/api/employees/{AtSiteA}", edit);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == AtSiteA));
        Assert.Equal("Shift supervisor", saved.JobTitle);
        Assert.Equal(DeptA, saved.DepartmentId);
        Assert.Equal(AdaPhone, saved.Phone);
    }

    private static string? Str(JsonElement row, string name) =>
        row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetString();

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    private static EffectiveDataScope Sites(params Guid[] siteIds) =>
        new(DataScopeKind.Sites, new HashSet<Guid>(siteIds), new HashSet<Guid>(), null);

    private static EffectiveDataScope Departments(params Guid[] departmentIds) =>
        new(DataScopeKind.Departments, new HashSet<Guid>(), new HashSet<Guid>(departmentIds), null);

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = SiteA, Name = "North" });
        db.Sites.Add(new Site { Id = SiteB, Name = "South" });
        db.Departments.Add(new Department { Id = DeptA, Name = "Assembly", SiteId = SiteA });
        db.Departments.Add(new Department { Id = DeptA2, Name = "Packing", SiteId = SiteA });
        db.Departments.Add(new Department { Id = DeptB, Name = "Assembly", SiteId = SiteB });
        db.Employees.Add(new Employee
        {
            Id = AtSiteA, Code = "E1001", FirstName = "Ada", LastName = "North",
            Phone = AdaPhone, SiteId = SiteA, DepartmentId = DeptA, EmployedFrom = new DateOnly(2024, 1, 15),
        });
        db.Employees.Add(new Employee
        {
            Id = AtSiteB, Code = "E1002", FirstName = "Bob", LastName = "South",
            SiteId = SiteB, DepartmentId = DeptB, EmployedFrom = new DateOnly(2024, 1, 15),
        });
    }
}
