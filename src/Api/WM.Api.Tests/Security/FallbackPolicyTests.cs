using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// The transport-level half of 003 P2a: what an anonymous caller actually gets back.
///
/// Every assertion here goes through the real middleware — a 401 is produced by the pipeline, not
/// by anything a unit test could stub. The probe endpoints are mapped the way the defect
/// describes it: <c>MapGet</c> and nothing else, no <c>RequireAuthorization</c>.
///
/// This is the test that would have caught PHASE-AUDIT.md A2.
/// </summary>
public sealed class FallbackPolicyTests
{
    private const string Unannotated = "/probe/unannotated";
    private const string Anonymous = "/probe/anonymous";

    private static Task<ApiTestHost> StartAsync() => ApiTestHost.StartAsync(app =>
    {
        app.MapGet(Unannotated, () => Results.Ok("reached"));
        app.MapGet(Anonymous, () => Results.Ok("reached")).AllowAnonymous();
    });

    [Fact]
    public async Task An_endpoint_mapped_without_RequireAuthorization_challenges_an_anonymous_caller()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(Unannotated);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // A challenge, not a bare 401: the caller is told how to authenticate, which is what
        // distinguishes "you are not signed in" from an endpoint answering 401 itself.
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.Select(h => h.Scheme));
    }

    [Fact]
    public async Task An_endpoint_mapped_without_RequireAuthorization_serves_an_authenticated_caller()
    {
        // The fallback authenticates; it does not authorize. A signed-in user holding no
        // permission at all still reaches an unannotated endpoint — so P2a is a floor under
        // RequireAuthorization, never a substitute for it.
        await using var host = await StartAsync();

        var response = await host.ClientWith().GetAsync(Unannotated);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_still_wins_over_the_fallback()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(Anonymous);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_still_answers_an_unauthenticated_caller()
    {
        // The one endpoint that was quietly relying on the absence of a fallback policy. It is
        // mapped by PlatformEndpoints, so this asserts the host's own code, not a copy of it.
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/hubs/attendance")]
    [InlineData("/hubs/attendance/negotiate")]
    public async Task The_realtime_transport_challenges_an_anonymous_caller(string route)
    {
        // Counted as a transport, not a route: /hubs/attendance was missed once by an audit that
        // enumerated Map{Get,Post,Put,Delete} only.
        await using var host = await StartAsync();

        var response = await host.Client.PostAsync(route, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_realtime_transport_refuses_an_authenticated_caller_without_the_permission()
    {
        // P1 requires attendance.view on the hub rather than mere authentication. Asserted here
        // because the fallback policy would satisfy "authenticated" and quietly mask its loss.
        await using var host = await StartAsync();

        var response = await host.ClientWith(WmPermissions.SelfService)
            .PostAsync("/hubs/attendance/negotiate", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_permissioned_endpoint_still_separates_unauthenticated_from_unauthorized()
    {
        await using var host = await StartAsync();

        var anonymous = await host.Client.GetAsync("/api/employees/");
        var wrongPermission = await host.ClientWith(WmPermissions.SelfService).GetAsync("/api/employees/");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongPermission.StatusCode);
    }

    [Fact]
    public async Task A_path_that_matches_no_endpoint_challenges_instead_of_answering_404()
    {
        // A consequence of the fallback policy worth stating rather than discovering: the
        // authorization middleware applies it to unmatched requests too, so an anonymous caller
        // can no longer probe which routes exist.
        await using var host = await StartAsync();

        var anonymous = await host.Client.GetAsync("/api/does-not-exist");
        var authenticated = await host.ClientWith().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);
    }

    [Fact]
    public async Task A_browser_preflight_is_not_challenged()
    {
        // A preflight carries no Authorization header. CORS runs before authorization and
        // short-circuits it; if that ordering ever changes, default-deny breaks every
        // cross-origin call from the portal — which would look like a CORS bug, not this.
        await using var host = await StartAsync();

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/employees/");
        preflight.Headers.Add("Origin", ApiTestHost.SpaOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await host.Client.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(ApiTestHost.SpaOrigin, response.Headers.GetValues("Access-Control-Allow-Origin"));
    }
}
