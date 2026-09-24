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
/// 008 P2 at the transport: the employee list projects each employee's resolved zone, and an
/// administrator sets or clears a site's zone through <c>PUT /api/sites/{id}/time-zone</c>, which
/// accepts IANA ids only. Which permission guards that route is pinned against the composed host by
/// <c>EndpointAuthorizationInventoryTests</c>, not here (see <see cref="PeopleEndpointHost"/>).
/// </summary>
public sealed class SiteTimeZoneEndpointTests
{
    private static readonly Guid Hq = Guid.Parse("21111111-1111-1111-1111-111111111111");
    private static readonly Guid Branch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Tashkent = Guid.Parse("23333333-3333-3333-3333-333333333333");
    private static readonly Guid Unset = Guid.Parse("24444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Two_sites_in_different_zones_resolve_differently_in_one_response()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        var rows = await RowsAsync(client);

        Assert.Equal(("America/Los_Angeles", "Site"), rows["E1"]);
        Assert.Equal(("America/Los_Angeles", "AncestorSite"), rows["E2"]);
        Assert.Equal(("Asia/Tashkent", "Site"), rows["E3"]);
        Assert.Equal((PeopleEndpointHost.InstallationDefault.Id, "Installation"), rows["E4"]);
    }

    [Fact]
    public async Task An_administrator_sets_a_zone_and_the_employee_list_follows_it()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var admin = host.ClientWith(WmPermissions.SitesManage, WmPermissions.EmployeesView);

        var response = await admin.PutAsJsonAsync($"/api/sites/{Branch}/time-zone", new { timeZone = "Pacific/Auckland" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pacific/Auckland", body.GetProperty("resolvedTimeZone").GetString());
        Assert.Equal("Site", body.GetProperty("resolvedTimeZoneSource").GetString());
        Assert.Equal("Pacific/Auckland", host.Read(db => db.Sites.AsNoTracking().Single(s => s.Id == Branch).TimeZone));
        Assert.Equal(("Pacific/Auckland", "Site"), (await RowsAsync(admin))["E2"]);
    }

    [Fact]
    public async Task Clearing_a_zone_makes_the_site_inherit()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var admin = host.ClientWith(WmPermissions.SitesManage);

        var response = await admin.PutAsJsonAsync($"/api/sites/{Tashkent}/time-zone", new { timeZone = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PeopleEndpointHost.InstallationDefault.Id, body.GetProperty("resolvedTimeZone").GetString());
        Assert.Equal("Installation", body.GetProperty("resolvedTimeZoneSource").GetString());
        Assert.Null(host.Read(db => db.Sites.AsNoTracking().Single(s => s.Id == Tashkent).TimeZone));
    }

    [Theory]
    [InlineData("Europe/Nowhere")]
    [InlineData("Central European Standard Time")] // a Windows id: resolvable on Windows, still refused
    [InlineData("")]
    [InlineData(" Asia/Tashkent")]
    public async Task A_value_that_is_not_an_IANA_zone_is_a_400_and_changes_nothing(string value)
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var admin = host.ClientWith(WmPermissions.SitesManage);

        var response = await admin.PutAsJsonAsync($"/api/sites/{Hq}/time-zone", new { timeZone = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("America/Los_Angeles", host.Read(db => db.Sites.AsNoTracking().Single(s => s.Id == Hq).TimeZone));
    }

    [Fact]
    public async Task A_site_outside_the_callers_scope_is_a_404()
    {
        var scope = new EffectiveDataScope(DataScopeKind.Sites, new HashSet<Guid> { Hq }, new HashSet<Guid>(), null);
        await using var host = await PeopleEndpointHost.StartAsync(scope, Seed);
        var admin = host.ClientWith(WmPermissions.SitesManage);

        var response = await admin.PutAsJsonAsync($"/api/sites/{Tashkent}/time-zone", new { timeZone = "UTC" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Asia/Tashkent", host.Read(db => db.Sites.AsNoTracking().Single(s => s.Id == Tashkent).TimeZone));
    }

    [Fact]
    public async Task Managing_employees_does_not_confer_the_right_to_set_a_zone()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var hr = host.ClientWith(WmPermissions.EmployeesView, WmPermissions.EmployeesManage);

        var response = await hr.PutAsJsonAsync($"/api/sites/{Tashkent}/time-zone", new { timeZone = "UTC" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<Dictionary<string, (string?, string?)>> RowsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/employees?pageSize=200");
        return page.GetProperty("items").EnumerateArray().ToDictionary(
            r => r.GetProperty("code").GetString()!,
            r => (r.GetProperty("timeZone").GetString(), r.GetProperty("timeZoneSource").GetString()));
    }

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.AddRange(
            new Site { Id = Hq, Name = "HQ", TimeZone = "America/Los_Angeles" },
            new Site { Id = Branch, Name = "Branch", ParentId = Hq },
            new Site { Id = Tashkent, Name = "Tashkent", TimeZone = "Asia/Tashkent" },
            new Site { Id = Unset, Name = "Unset" });
        var from = new DateOnly(2024, 1, 15);
        db.Employees.AddRange(
            new Employee { Code = "E1", FirstName = "A", LastName = "A", SiteId = Hq, EmployedFrom = from },
            new Employee { Code = "E2", FirstName = "B", LastName = "B", SiteId = Branch, EmployedFrom = from },
            new Employee { Code = "E3", FirstName = "C", LastName = "C", SiteId = Tashkent, EmployedFrom = from },
            new Employee { Code = "E4", FirstName = "D", LastName = "D", SiteId = Unset, EmployedFrom = from });
    }
}
