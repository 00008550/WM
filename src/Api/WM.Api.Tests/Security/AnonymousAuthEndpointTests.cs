using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// The other half of 003 P2a's "done when": the three <c>AllowAnonymous</c> endpoints still work
/// under a default-deny fallback policy.
///
/// These go all the way through <c>AuthService</c> to a real database, because a login that is
/// merely "not 401" proves nothing here — <c>/api/auth/login</c> answers 401 itself on bad
/// credentials, so the status code alone cannot tell a rejected sign-in from a blocked request.
/// A 200 carrying tokens can.
/// </summary>
public sealed class AnonymousAuthEndpointTests
{
    private static Task<ApiTestHost> StartAsync() =>
        ApiTestHost.StartAsync(withDatabases: true);

    [Fact]
    public async Task Login_answers_an_anonymous_caller()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync("/api/auth/login", Credentials);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Contains(
            ApiTestHost.SeededPermission,
            body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Login_still_rejects_bad_credentials_on_its_own_terms()
    {
        // The 401 that is the endpoint's answer, not the pipeline's: it carries problem details
        // and no challenge header. Worth pinning, because the fallback policy's 401 looks the
        // same from the status line alone.
        await using var host = await StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/api/auth/login", new { userName = ApiTestHost.SeededUserName, password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task Refresh_answers_an_anonymous_caller()
    {
        await using var host = await StartAsync();
        var refreshToken = (await SignInAsync(host)).GetProperty("refreshToken").GetString();

        var response = await host.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_answers_an_anonymous_caller()
    {
        await using var host = await StartAsync();
        var refreshToken = (await SignInAsync(host)).GetProperty("refreshToken").GetString();

        var response = await host.Client.PostAsJsonAsync("/api/auth/logout", new { refreshToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_a_real_sign_in_reaches_a_protected_endpoint()
    {
        // Sign in anonymously, then use what you were given. This is the round trip the portal
        // makes, and the one thing that would break loudly if the fallback policy were wrong
        // about what "authenticated" means.
        await using var host = await StartAsync();
        var accessToken = (await SignInAsync(host)).GetProperty("accessToken").GetString();

        using var client = host.Client;
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var response = await client.GetAsync("/api/auth/me");
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApiTestHost.SeededUserName, me.GetProperty("userName").GetString());
        Assert.Contains(
            WmPermissions.EmployeesView,
            me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    private static object Credentials => new
    {
        userName = ApiTestHost.SeededUserName,
        password = ApiTestHost.SeededPassword,
    };

    private static async Task<JsonElement> SignInAsync(ApiTestHost host)
    {
        var response = await host.Client.PostAsJsonAsync("/api/auth/login", Credentials);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
