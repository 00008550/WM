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
///
/// <para>
/// <b>Grown on 2026-08-11 (003 P2b review) to pin the permission, not merely its presence.</b>
/// Until then the strongest thing said here was "some registered policy is attached", which is
/// silent about <em>which</em> — and a permission change that costs no other test nothing at all.
/// Measured that day: moving <c>GET /api/sites</c> from <c>employees.view</c> to
/// <c>employees.manage</c> failed no test in the repository. The employee writes 003 P2b scoped
/// were better off only by accident — downgrading them does trip
/// <c>WM.Modules.People.Tests</c>, but collaterally, because that project's callers mint exactly
/// the permission they name, so the endpoint refuses them and the failure reads "Expected:
/// Created, Actual: Forbidden" — a scope defect, not the policy change it actually is.
/// <see cref="RequiredPermissions"/> makes the permission itself the assertion, for the whole
/// surface rather than for the two endpoints that happened to be under review.
/// </para>
/// </summary>
public sealed class EndpointAuthorizationInventoryTests
{
    /// <summary>
    /// The complete anonymous surface. Sign-in cannot require a token, and /health is asked by
    /// things that hold no credentials (load balancers, probes, uptime monitors).
    ///
    /// <para>
    /// <b>Grown by one on 2026-08-06 (plan 006 P2), deliberately.</b> <c>/api/health/ready</c> is
    /// the fifth entry. It is anonymous for the same reason as the fourth — it is asked by an
    /// external smoke check running from GitHub against the public URL, which holds no
    /// credentials, and by a container health check that holds none either — and it is under
    /// <c>/api/</c> because that is the only prefix the portal's nginx proxies. What it may
    /// disclose was decided with it: status and per-database migration counts, plus a build
    /// identity that is <em>empty unless a deployment sets it</em>, and never an exception
    /// message. <see cref="ReadinessEndpointTests"/> holds those assertions.
    /// </para>
    /// </summary>
    private static readonly string[] DeliberatelyAnonymous =
    [
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/health",
        "/api/health/ready",
    ];

    /// <summary>
    /// Anonymous only on a development host that opted in (plan 011 P9): the password-less agent
    /// sign-in. Named here rather than folded into <see cref="DeliberatelyAnonymous"/> because it
    /// is never on the production surface — the tests above compose Production, where the route
    /// does not exist — and <see cref="The_dev_only_exemption_is_the_only_addition_on_a_development_host"/>
    /// proves it is the one thing a development host adds.
    /// </summary>
    private static readonly string[] DevOnlyAnonymous =
    [
        "/api/dev/sign-in",
    ];

    /// <summary>
    /// Every non-anonymous transport and the permission it requires. Complete: a new endpoint fails
    /// <see cref="Every_authorized_transport_requires_the_permission_we_chose_for_it"/> until it is
    /// named here, for the same reason <see cref="DeliberatelyAnonymous"/> is complete.
    ///
    /// <para>
    /// The permissions are written as literals rather than as <see cref="WmPermissions"/> constants
    /// on purpose. The string is the contract — it is what a role grant stores and what an issued
    /// token carries — so renaming a constant's <em>value</em> is exactly the change that should
    /// land here for a reviewer to see, not one the pin should silently follow.
    /// </para>
    ///
    /// <para>
    /// The key is the transport, not the route: <c>GET</c> and <c>PUT</c> on one path are two
    /// decisions, and the hub's two legs are two endpoints (see
    /// <see cref="Both_legs_of_the_realtime_transport_require_the_attendance_permission"/>, which
    /// keeps saying so in its own words). <see cref="AuthenticatedOnly"/> is the deliberate empty
    /// entry.
    /// </para>
    /// </summary>
    private static readonly (string Transport, string Permission)[] RequiredPermissions =
    [
        // Identity — sign-in itself is anonymous; everything after it is not.
        ("GET /api/auth/me", AuthenticatedOnly),
        ("GET /api/users", "users.manage"),
        ("POST /api/users", "users.manage"),
        ("PUT /api/users/{id:guid}", "users.manage"),
        ("GET /api/users/roles", "users.manage"),
        ("POST /api/users/{id:guid}/reset-password", "users.manage"),
        ("GET /api/users/{id:guid}/security-groups", "users.manage"),
        ("PUT /api/users/{id:guid}/security-groups", "users.manage"),
        ("GET /api/access-diagnostics/{userId:guid}", "users.manage"),
        ("GET /api/security-groups", "roles.manage"),
        ("POST /api/security-groups", "roles.manage"),
        ("PUT /api/security-groups/{id:guid}", "roles.manage"),
        ("DELETE /api/security-groups/{id:guid}", "roles.manage"),

        // People. The two writes are the pair 003 P2b scoped: the split between view and manage is
        // load-bearing, since employees.view is the permission an ordinary supervisor holds.
        ("GET /api/employees", "employees.view"),
        ("GET /api/employees/{id:guid}", "employees.view"),
        ("POST /api/employees", "employees.manage"),
        ("PUT /api/employees/{id:guid}", "employees.manage"),
        ("GET /api/sites", "employees.view"),
        // 003 P3: the department picker's source. Same permission as sites, and scoped the same way.
        ("GET /api/departments", "employees.view"),
        // The leaving-reason vocabulary reads with employees.view, not employees.manage: it is a
        // lookup a viewer needs in order to render why someone left, and it discloses nothing about
        // any person. Maintaining the list — which does not exist yet — is a separate decision and
        // will want employees.manage or an administration permission of its own (007 P1 As built).
        ("GET /api/leaving-reasons", "employees.view"),

        // Time & attendance.
        ("GET /api/attendance/live", "attendance.view"),
        ("GET /api/attendance/timesheet/{employeeId:guid}", "attendance.view"),
        ("GET /api/punches/recent", "attendance.view"),
        ("POST /api/punches", "punches.record"),
        ("/hubs/attendance", "attendance.view"),
        ("/hubs/attendance/negotiate", "attendance.view"),

        // Self-service: one permission for the whole "my own record" surface, because every one of
        // these reads its subject from the token claim rather than from the request.
        ("GET /api/me/employee", "selfservice.access"),
        ("GET /api/me/punches", "selfservice.access"),
        ("GET /api/me/timesheet", "selfservice.access"),
        ("POST /api/me/punch", "selfservice.access"),
    ];

    /// <summary>
    /// <c>RequireAuthorization()</c> with no argument: signed in, no permission. Written as a named
    /// constant so the empty string in the table above reads as a decision rather than a gap.
    /// </summary>
    private const string AuthenticatedOnly = "";

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
    public async Task The_dev_only_exemption_is_the_only_addition_on_a_development_host()
    {
        await using var host = ApiTestHost.Compose(
            environmentName: Microsoft.Extensions.Hosting.Environments.Development,
            configurationOverrides: new Dictionary<string, string?> { ["DevSignIn:Enabled"] = "true" });

        var anonymous = host.Endpoints
            .Where(IsAnonymous)
            .Select(Route)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(DeliberatelyAnonymous.Concat(DevOnlyAnonymous).Order(StringComparer.Ordinal).ToArray(), anonymous);
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

    [Fact]
    public async Task Every_authorized_transport_requires_the_permission_we_chose_for_it()
    {
        // "Which policy", not "a policy". The test above proves an endpoint carries something and
        // the one below proves the name resolves; neither notices employees.manage becoming
        // employees.view, which is a silent grant of write access to every viewer in the estate.
        await using var host = ApiTestHost.Compose();

        var actual = host.Endpoints
            .Where(e => !IsAnonymous(e))
            .Select(e => $"{Transport(e)} -> {string.Join(", ", Policies(e).Distinct().Order(StringComparer.Ordinal))}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = RequiredPermissions
            .Select(x => $"{x.Transport} -> {x.Permission}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
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

    /// <summary>
    /// The route, prefixed by the methods that reach it. A hub leg carries no method metadata, so
    /// it is named by route alone rather than by an empty prefix.
    /// </summary>
    private static string Transport(Endpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return methods.Count == 0
            ? Route(endpoint)
            : $"{string.Join('|', methods.Order(StringComparer.Ordinal))} {Route(endpoint)}";
    }

    private static string Route(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
            ? "/" + (route.RoutePattern.RawText ?? string.Empty).Trim('/')
            : endpoint.DisplayName ?? endpoint.ToString()!;
}
