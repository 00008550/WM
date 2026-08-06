using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using WM.Api.Infrastructure;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// 006 P3's second half: what one unauthenticated address may spend.
///
/// <para>
/// The behavioural assertions go through <see cref="ApiTestHost"/>, which composes the edge by
/// calling <c>PublicEdge.UseWmPublicEdge</c> — the same method <c>Program.cs</c> calls. That is
/// deliberate and it is what makes these tests load-bearing: remove <c>UseRateLimiter</c> from the
/// product, or the policy from an endpoint, and these fail.
/// </para>
///
/// <para>
/// The inventory assertion is the other half, in the shape
/// <see cref="EndpointAuthorizationInventoryTests"/> established: the limited surface is
/// enumerated from what the host actually maps, so adding or losing a limit is a decision with a
/// reviewer attached rather than a silent change.
/// </para>
/// </summary>
public sealed class RateLimitTests
{
    /// <summary>
    /// Every transport that carries the anonymous limit — the three sign-in endpoints named by the
    /// plan, plus readiness, which 006 P2's review handed to this portion: it is anonymous,
    /// uncached and costs three database round trips per module per request.
    ///
    /// <para>
    /// What is <em>not</em> here is as deliberate: <c>/health</c> (an orchestrator restarts on it),
    /// <c>/hubs/attendance</c>, and every authenticated endpoint.
    /// </para>
    /// </summary>
    private static readonly string[] DeliberatelyRateLimited =
    [
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
        WmHealthChecks.ReadinessPath,
    ];

    /// <summary>The portal's nginx, on the compose bridge: the peer, never the visitor.</summary>
    private const string Nginx = "172.20.0.5";

    private const string Flooder = "198.51.100.7";
    private const string Bystander = "198.51.100.8";

    [Fact]
    public async Task Sign_in_attempts_over_the_limit_are_refused_with_429()
    {
        await using var host = await StartAsync(permitLimit: 2, withDatabases: true);
        using var client = host.Client;

        var first = await SignInAsync(client);
        var second = await SignInAsync(client);
        var third = await SignInAsync(client);

        // 401 is the endpoint answering on its own terms; 429 is the edge answering before it.
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        // Not 503, which is what the framework answers by default and would tell a client this
        // host is broken rather than that they are going too fast.
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, third.StatusCode);
    }

    [Fact]
    public async Task Two_visitors_behind_one_proxy_do_not_share_a_budget()
    {
        // The test that would have caught the trap. Every request here arrives from nginx, so
        // without UseForwardedHeaders all four would land in one bucket, the bystander's request
        // would be 429, and the "limiter" would be a global tap on the sign-in endpoint.
        await using var host = await StartAsync(permitLimit: 2, withDatabases: true);
        using var client = host.ClientFrom(Nginx);

        var flood = new[]
        {
            (await SignInAsync(client, Flooder)).StatusCode,
            (await SignInAsync(client, Flooder)).StatusCode,
            (await SignInAsync(client, Flooder)).StatusCode,
        };

        Assert.Equal(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
            flood);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, Bystander)).StatusCode);

        // And the flooder is still out of budget: two independent buckets, not one that reset.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(client, Flooder)).StatusCode);
    }

    [Fact]
    public async Task A_refused_request_says_when_to_come_back()
    {
        await using var host = await StartAsync(permitLimit: 1, withDatabases: true);
        using var client = host.Client;

        await SignInAsync(client);
        var refused = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    [Fact]
    public async Task Readiness_shares_the_anonymous_budget()
    {
        // Carried over from 006 P2's review rather than rediscovered: readiness is anonymous and
        // uncached, so an unauthenticated flood against it is a flood against Postgres.
        await using var host = await StartAsync(permitLimit: 1);
        using var client = host.Client;

        var first = await client.GetAsync(WmHealthChecks.ReadinessPath);
        var second = await client.GetAsync(WmHealthChecks.ReadinessPath);

        // No database in this host, so ready is 503 — the point is that the second is refused by
        // the limiter and not by the check.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Liveness_is_never_rate_limited()
    {
        // An orchestrator restarts containers on this endpoint. Throttling it would turn a flood
        // into a rolling restart of the thing being flooded.
        await using var host = await StartAsync(permitLimit: 1);
        using var client = host.Client;

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(WmHealthChecks.LivenessPath)).StatusCode);
    }

    [Fact]
    public async Task Authenticated_traffic_is_not_rate_limited_by_this_policy()
    {
        // The limit exists because an anonymous caller has proved nothing. A signed-in user is a
        // different problem with a different answer, and the plan says so explicitly.
        await using var host = await StartAsync(permitLimit: 1);
        using var client = host.ClientWith();

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task The_rate_limited_surface_is_exactly_the_one_we_decided_on()
    {
        await using var host = ApiTestHost.Compose();

        var limited = host.Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
            .Select(Route)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DeliberatelyRateLimited.Order(StringComparer.Ordinal).ToArray(), limited);
    }

    [Fact]
    public async Task Every_rate_limited_endpoint_names_a_policy_that_exists()
    {
        // A policy name with no registration is a 500 at request time, not a 429 — the same class
        // of typo Every_authorized_endpoint_names_a_permission_policy_that_exists catches.
        await using var host = ApiTestHost.Compose();

        var unknown = host.Endpoints
            .Select(endpoint => (
                Route: Route(endpoint),
                Policy: endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName))
            .Where(x => x.Policy is not null && !WmRateLimits.All.Contains(x.Policy))
            .Select(x => $"{x.Route} -> {x.Policy}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], unknown);
    }

    [Fact]
    public void A_permit_limit_that_refuses_everything_is_refused()
    {
        var fault = Assert.Throws<InvalidOperationException>(() => ApiTestHost.Compose(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["RateLimiting:Anonymous:PermitLimit"] = "0",
            }));

        Assert.Contains("RateLimiting__Anonymous__PermitLimit", fault.Message, StringComparison.Ordinal);
    }

    private static Task<ApiTestHost> StartAsync(int permitLimit, bool withDatabases = false) =>
        ApiTestHost.StartAsync(
            withDatabases: withDatabases,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["RateLimiting:Anonymous:PermitLimit"] =
                    permitLimit.ToString(CultureInfo.InvariantCulture),
                // Long enough that no test can race the window rolling over.
                ["RateLimiting:Anonymous:Window"] = "00:10:00",
            });

    /// <summary>
    /// A failed sign-in — the request a credential-stuffing bot makes. Wrong password on purpose:
    /// the interesting distinction is 401 (the endpoint answered) versus 429 (the edge did).
    /// </summary>
    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                userName = ApiTestHost.SeededUserName,
                password = "not-the-password",
            }),
        };

        if (forwardedFor is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
    }

    private static string Route(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
            ? "/" + (route.RoutePattern.RawText ?? string.Empty).Trim('/')
            : endpoint.DisplayName ?? endpoint.ToString()!;
}
