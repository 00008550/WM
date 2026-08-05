using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// The inventory half of 003 P2a: not "does an unannotated endpoint 401" (FallbackPolicyTests
/// answers that) but "which transports are anonymous, and did anyone mean them to be".
///
/// It enumerates what the host actually maps — every module's endpoints plus
/// <see cref="Infrastructure.PlatformEndpoints"/> — rather than a list somebody maintains by
/// hand. A new endpoint with no policy fails the first test; a new <c>AllowAnonymous</c> fails
/// the second until it is named here, which is the point: anonymity becomes a decision with a
/// reviewer attached.
/// </summary>
public sealed class EndpointAuthorizationInventoryTests
{
    /// <summary>
    /// The complete anonymous surface. Sign-in cannot require a token, and /health is asked by
    /// things that hold no credentials (load balancers, probes, uptime monitors).
    /// </summary>
    private static readonly string[] DeliberatelyAnonymous =
    [
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/health",
    ];

    [Fact]
    public async Task Every_mapped_endpoint_is_authorized_or_deliberately_anonymous()
    {
        await using var host = ApiTestHost.Compose();

        var unprotected = host.Endpoints
            .Where(e => !IsAnonymous(e) && !IsAuthorized(e))
            .Select(Route)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], unprotected);
    }

    [Fact]
    public async Task The_anonymous_surface_is_exactly_the_one_we_decided_on()
    {
        await using var host = ApiTestHost.Compose();

        var anonymous = host.Endpoints
            .Where(IsAnonymous)
            .Select(Route)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DeliberatelyAnonymous.Order(StringComparer.Ordinal).ToArray(), anonymous);
    }

    [Fact]
    public async Task Both_legs_of_the_realtime_transport_require_the_attendance_permission()
    {
        // MapHub is two endpoints — negotiate and the socket itself. Asserting the pair is the
        // "count transports, not routes" rule in executable form.
        await using var host = ApiTestHost.Compose();

        var hub = host.Endpoints.Where(e => Route(e).StartsWith("/hubs/", StringComparison.Ordinal)).ToArray();

        Assert.Contains("/hubs/attendance", hub.Select(Route));
        Assert.Contains("/hubs/attendance/negotiate", hub.Select(Route));
        Assert.All(hub, e => Assert.Contains(WmPermissions.AttendanceView, Policies(e)));
    }

    [Fact]
    public async Task Every_authorized_endpoint_names_a_permission_policy_that_exists()
    {
        // A policy name that is not registered fails at request time with a 500, not a 403 — the
        // kind of typo the fallback policy cannot save anyone from.
        await using var host = ApiTestHost.Compose();

        var unknown = host.Endpoints
            .SelectMany(e => Policies(e).Select(policy => (Route: Route(e), Policy: policy)))
            .Where(x => !WmPermissions.All.Contains(x.Policy))
            .Select(x => $"{x.Route} -> {x.Policy}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], unknown);
    }

    private static bool IsAnonymous(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private static bool IsAuthorized(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null ||
        endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is not null;

    /// <summary>Named policies only — <c>RequireAuthorization()</c> with no argument has none.</summary>
    private static IEnumerable<string> Policies(Endpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p))!;

    private static string Route(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
            ? "/" + (route.RoutePattern.RawText ?? string.Empty).Trim('/')
            : endpoint.DisplayName ?? endpoint.ToString()!;
}
