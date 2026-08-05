using System.Net;
using System.Text.Json;
using WM.Api.Infrastructure;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// 006 P2's second half. <c>/health</c> was registered with no checks at all, so it answered
/// <c>Healthy</c> with the database on fire — and the container's <c>HEALTHCHECK</c> pointed at
/// it. Readiness is the endpoint that can fail; liveness is asserted here too, because "still
/// cannot fail" is now a decision rather than an oversight.
///
/// It sits under <c>Security/</c> with the rest because it is the host's fifth anonymous
/// transport: what it may disclose to an unauthenticated caller is the interesting question about
/// it, and <see cref="EndpointAuthorizationInventoryTests"/> names it for the same reason.
/// </summary>
public sealed class ReadinessEndpointTests
{
    [Fact]
    public async Task Readiness_answers_200_when_every_database_is_connected_and_migrated()
    {
        await using var host = await ApiTestHost.StartAsync(withDatabases: true);

        var response = await host.Client.GetAsync(WmHealthChecks.ReadinessPath);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());

        // Every module's database, not just one: a host that could reach Identity and nothing
        // else used to look identical from outside.
        var checks = body.GetProperty("checks");
        foreach (var name in new[] { "database:identity", "database:people", "database:time-attendance" })
        {
            var check = checks.GetProperty(name);
            Assert.Equal("Healthy", check.GetProperty("status").GetString());
            Assert.Equal(0, check.GetProperty("pending").GetInt32());
            Assert.True(check.GetProperty("applied").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task Readiness_does_not_answer_200_without_a_database()
    {
        // The default host points at 127.0.0.1:1 — a port nothing listens on. This is the case the
        // old /health got wrong.
        await using var host = await ApiTestHost.StartAsync();

        var response = await host.Client.GetAsync(WmHealthChecks.ReadinessPath);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_says_nothing_about_the_database_it_could_not_reach()
    {
        // The endpoint is anonymous, so an Npgsql failure message — which names the host, the
        // database and the user — must not reach the body.
        await using var host = await ApiTestHost.StartAsync();

        var body = await (await host.Client.GetAsync(WmHealthChecks.ReadinessPath)).Content
            .ReadAsStringAsync();

        Assert.DoesNotContain("wm_never_opened", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readiness_omits_the_build_identity_when_it_is_not_configured()
    {
        // A customer install discloses nothing. The default is empty in appsettings.json, and
        // empty must mean absent rather than an empty string.
        await using var host = await ApiTestHost.StartAsync();

        var body = await ReadJsonAsync(await host.Client.GetAsync(WmHealthChecks.ReadinessPath));

        Assert.False(body.TryGetProperty("build", out _));
    }

    [Fact]
    public async Task Readiness_reports_the_build_identity_when_a_deployment_sets_one()
    {
        // The demo does set it, because a smoke check that cannot tell which build answered
        // cannot tell a silently-failed image pull from a good deploy.
        const string sha = "0f1e2d3c4b5a";
        await using var host = await ApiTestHost.StartAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                [WmHealthChecks.BuildIdentityKey] = sha,
            });

        var body = await ReadJsonAsync(await host.Client.GetAsync(WmHealthChecks.ReadinessPath));

        Assert.Equal(sha, body.GetProperty("build").GetString());
    }

    [Fact]
    public async Task Liveness_still_answers_when_no_database_is_reachable()
    {
        // Deliberate, not left over: an orchestrator restarts on liveness, and a database outage
        // should not make every replica get killed and rescheduled.
        await using var host = await ApiTestHost.StartAsync();

        var response = await host.Client.GetAsync(WmHealthChecks.LivenessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_answers_an_anonymous_caller_rather_than_challenging_one()
    {
        // The smoke check runs from GitHub against the public URL and holds no credentials.
        await using var host = await ApiTestHost.StartAsync(withDatabases: true);

        var response = await host.Client.GetAsync(WmHealthChecks.ReadinessPath);

        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Readiness_is_under_the_prefix_nginx_proxies()
    {
        // nginx.conf forwards /api/ and /hubs/ and nothing else, so a readiness endpoint anywhere
        // else is unreachable from outside and the smoke check would need an nginx change.
        Assert.StartsWith("/api/", WmHealthChecks.ReadinessPath, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
