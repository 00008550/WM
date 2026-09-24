using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// Plan 011 P9: <c>POST /api/dev/sign-in</c>, a password bypass by design. Its whole safety is the
/// double gate (Development <em>and</em> <c>DevSignIn:Enabled</c>) plus the boot guard, so those
/// are what this file is mostly about.
///
/// <para>
/// Every test sets <c>DevSignIn:Enabled</c> explicitly. WM.Api's appsettings.Development.json is
/// copied next to this assembly and turns it on, so a Development host composed here without an
/// override would be testing that file rather than the gate.
/// </para>
/// </summary>
public sealed class DevSignInTests
{
    private const string Route = "/api/dev/sign-in";

    [Fact]
    public async Task Development_with_the_flag_issues_tokens_that_work()
    {
        await using var host = await ApiTestHost.StartAsync(
            withDatabases: true, environmentName: Environments.Development, configurationOverrides: Flag("true"));

        var response = await host.Client.PostAsJsonAsync(Route, new { userName = ApiTestHost.SeededUserName });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The access token authorizes a real endpoint.
        using var client = host.Client;
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        var me = await client.GetAsync("/api/auth/me");
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(ApiTestHost.SeededUserName, meBody.GetProperty("userName").GetString());
        Assert.Contains(
            WmPermissions.EmployeesView,
            meBody.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        // The refresh token was stored hashed by the same path login uses, so refresh accepts it.
        var refresh = await host.Client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = body.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Development_without_the_flag_does_not_map_the_route()
    {
        await using var host = await ApiTestHost.StartAsync(
            withDatabases: true, environmentName: Environments.Development, configurationOverrides: Flag("false"));

        var response = await PostAsSignedInAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(host.Endpoints, IsDevSignIn);
    }

    [Fact]
    public async Task Production_without_the_flag_does_not_map_the_route()
    {
        await using var host = await ApiTestHost.StartAsync(
            withDatabases: true, environmentName: Environments.Production, configurationOverrides: Flag(null));

        var response = await PostAsSignedInAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(host.Endpoints, IsDevSignIn);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_flag_outside_Development_stops_the_host_booting(string environment)
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(environmentName: environment, configurationOverrides: Flag("true")));

        Assert.Contains("DevSignIn:Enabled", fault.Message, StringComparison.Ordinal);
        Assert.Contains("DevSignIn__Enabled", fault.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void The_route_is_mapped_only_when_both_gates_are_open(bool enabled, bool isDevelopment, bool mapped)
    {
        // The environment half cannot be reached end to end while the boot guard stands — a
        // non-Development host with the flag never gets as far as mapping — so the predicate the
        // mapping asks is pinned directly. Removing either gate from it turns a row red.
        Assert.Equal(mapped, DevSignInOptions.ShouldMap(new DevSignInOptions { Enabled = enabled }, isDevelopment));
    }

    [Fact]
    public async Task Unknown_and_inactive_users_are_refused_identically()
    {
        await using var host = await ApiTestHost.StartAsync(
            withDatabases: true, environmentName: Environments.Development, configurationOverrides: Flag("true"));

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new User
            {
                UserName = "retired",
                Email = "retired@wm.local",
                DisplayName = "Retired",
                PasswordHash = "unused",
                IsActive = false,
            });
            await db.SaveChangesAsync();
        }

        var inactive = await host.Client.PostAsJsonAsync(Route, new { userName = "retired" });
        var unknown = await host.Client.PostAsJsonAsync(Route, new { userName = "nobody-by-this-name" });

        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        Assert.Equal(inactive.StatusCode, unknown.StatusCode);
        Assert.Equal(
            (await inactive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    /// <summary>
    /// As a signed-in caller, because that is the only caller for whom "the route does not exist"
    /// is observable: default-deny also covers unmatched paths, so an anonymous request to any
    /// route that is not mapped gets the fallback policy's 401. A signed-in one gets routing's 404.
    /// </summary>
    private static Task<HttpResponseMessage> PostAsSignedInAsync(ApiTestHost host) =>
        host.ClientWith().PostAsJsonAsync(Route, new { userName = ApiTestHost.SeededUserName });

    private static bool IsDevSignIn(Microsoft.AspNetCore.Http.Endpoint endpoint) =>
        endpoint is Microsoft.AspNetCore.Routing.RouteEndpoint route
        && "/" + route.RoutePattern.RawText?.Trim('/') == Route;

    private static Dictionary<string, string?> Flag(string? value) => new() { ["DevSignIn:Enabled"] = value };
}
