using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WM.Modules.People.Data;
using WM.SharedKernel.Security;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// The People module's endpoints, mapped into a host with one caller whose resolved scope the test
/// chooses. Over an in-memory transport and an in-memory database, so nothing opens a socket.
///
/// <para>
/// Two deliberate departures from <c>Program.cs</c>, both because this project tests one module
/// rather than the composed host:
/// </para>
/// <list type="bullet">
/// <item><b>The database is the in-memory provider</b>, not the Npgsql one
/// <see cref="PeopleModule.RegisterServices"/> registers — there is no Postgres harness in this
/// repository (<c>docs/plans/STATE.md</c>). So these tests say nothing about SQL; in particular the
/// case-insensitive employee-code probe and the unique index that disagrees with it (plan 007) are
/// out of reach here as well as out of scope.</item>
/// <item><b>Authentication is a test scheme</b> that mints the permissions a request asks for, over
/// the <i>real</i> permission policies built the way <c>IdentityModule</c> builds them. Which
/// permission each endpoint requires is decided against the composed host, by
/// <c>EndpointAuthorizationInventoryTests.Every_authorized_transport_requires_the_permission_we_chose_for_it</c>
/// — a route-by-route pin, so moving <c>POST /api/employees</c> off <c>employees.manage</c> fails
/// there by name. That cross-reference was written here before the pin existed and overstated it:
/// until 2026-08-11 the inventory asserted only that <i>some</i> registered policy was attached,
/// never which one. Nor can this project supply the missing half. The host below mints whatever
/// permission a test asks for, so a downgrade here surfaces only as collateral — every caller in
/// <c>EmployeeWriteScopeEndpointTests</c> holds exactly <c>employees.manage</c>, so an endpoint
/// demoted to <c>employees.view</c> refuses them and the failure reads as a scope defect. That is
/// the right shape for testing scope and the wrong instrument for testing policy.</item>
/// </list>
/// </summary>
internal sealed class PeopleEndpointHost(WebApplication app) : IAsyncDisposable
{
    /// <param name="callerEmployeeId">
    /// The employee the signed-in caller <i>is</i>, for the self-service endpoints. Null — the
    /// default — is an account with no employee record linked, which is the honest default for a
    /// project that mostly tests the write paths.
    /// </param>
    public static async Task<PeopleEndpointHost> StartAsync(
        EffectiveDataScope scope,
        Action<PeopleDbContext>? seed = null,
        Guid? callerEmployeeId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        // One store per host, so tests cannot see each other's employees. The name is computed here
        // and captured, not inside the lambda: AddDbContext builds its options per scope, so a name
        // generated in the lambda gives every request its own empty database — which looks exactly
        // like a seed that never ran.
        var databaseName = $"people-{Guid.CreateVersion7()}";
        builder.Services.AddDbContext<PeopleDbContext>(o => o.UseInMemoryDatabase(databaseName));

        builder.Services.AddScoped<IDataScopeResolver>(_ => new FixedScope(scope));
        // Only /api/me/employee resolves this, and it resolves it before touching the database.
        builder.Services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(callerEmployeeId));

        builder.Services.AddAuthentication(TestCaller.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestCaller>(TestCaller.SchemeName, null);

        builder.Services.AddAuthorization(o =>
        {
            // 003 P2a's fallback policy, because the endpoints under test are reached through the
            // same middleware it installed: a caller with no permissions must not arrive at a
            // handler and be refused by the scope check for the wrong reason.
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in WmPermissions.All)
                o.AddPolicy(permission, p => p.RequireClaim(WmPermissions.ClaimType, permission));
        });

        var app = builder.Build();
        new PeopleModule().MapEndpoints(app);

        if (seed is not null)
        {
            using var services = app.Services.CreateScope();
            var db = services.ServiceProvider.GetRequiredService<PeopleDbContext>();
            seed(db);
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        return new PeopleEndpointHost(app);
    }

    /// <summary>A signed-in caller holding exactly <paramref name="permissions"/>.</summary>
    public HttpClient ClientWith(params string[] permissions)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestCaller.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>Read the database back, outside any request. Untracked: the answer is what was saved.</summary>
    public T Read<T>(Func<PeopleDbContext, T> read)
    {
        using var services = app.Services.CreateScope();
        return read(services.ServiceProvider.GetRequiredService<PeopleDbContext>());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private sealed class FixedScope(EffectiveDataScope scope) : IDataScopeResolver
    {
        public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) =>
            Task.FromResult(scope);

        public Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(scope);
    }

    /// <summary>
    /// The caller as the self-service endpoints see them. <paramref name="employeeId"/> null is an
    /// account with no employee record; a value is the employee they are, which is the only way
    /// <c>/api/me/employee</c> can be reached — the id comes from the token, never the request.
    /// </summary>
    private sealed class TestCurrentUser(Guid? employeeId) : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserName => "tester";
        public Guid? EmployeeId => employeeId;
        public bool IsAuthenticated => true;
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public bool HasPermission(string permission) => false;
    }

    /// <summary>
    /// Authenticates every request that names its permissions in a header, with the same claim type
    /// the product's tokens carry — so the policies are exercised, not bypassed.
    /// </summary>
    private sealed class TestCaller(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string PermissionsHeader = "X-Test-Permissions";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(PermissionsHeader, out var header))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = header.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(WmPermissions.ClaimType, permission));

            var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
