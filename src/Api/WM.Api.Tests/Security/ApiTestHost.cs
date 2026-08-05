using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WM.Api.Infrastructure;
using WM.Modules.Identity;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using WM.Modules.People;
using WM.Modules.TimeAttendance;
using WM.SharedKernel.Modules;
using WM.SharedKernel.Security;

namespace WM.Api.Tests.Security;

/// <summary>
/// The API host composed the way <c>Program.cs</c> composes it — the same three modules, the same
/// CORS → authentication → authorization order, the same
/// <see cref="PlatformEndpoints.MapPlatformEndpoints"/> call — over an in-memory transport.
///
/// By default it opens no database at all. That is a feature: every authorization assertion is
/// decided by middleware, which runs before a handler can resolve a <c>DbContext</c>, so a request
/// that wrongly gets through fails loudly on connect instead of quietly returning 200.
/// <c>withIdentityDatabase</c> is the deliberate exception, for the sign-in path.
/// </summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    public const string Issuer = "wm-tests";
    public const string Audience = "wm-tests";
    public const string SigningKey = "wm-test-signing-key-of-at-least-32-bytes";
    public const string SpaOrigin = "http://localhost:4200";

    // The seeded account, present only when the host is composed with an Identity database.
    public const string SeededUserName = "tester";
    public const string SeededPassword = "Test-Password-1";
    public const string SeededPermission = WmPermissions.EmployeesView;

    private const string CorsPolicy = "spa";

    private static readonly JwtOptions Jwt = new()
    {
        Issuer = Issuer,
        Audience = Audience,
        SigningKey = SigningKey,
    };

    private readonly WebApplication _app;
    private readonly SqliteConnection? _identityDb;

    private ApiTestHost(WebApplication app, SqliteConnection? identityDb)
    {
        _app = app;
        _identityDb = identityDb;
    }

    /// <summary>Built and mapped, but not started — enough to enumerate what got mapped.</summary>
    /// <param name="mapProbes">Extra endpoints, mapped after the real ones.</param>
    /// <param name="withIdentityDatabase">
    /// Give the Identity module a real (SQLite, in-process) database and one seeded user, so
    /// sign-in can be exercised end to end. Off by default: the other tests are stronger for
    /// having no database at all, since a request that reaches a handler then fails loudly.
    /// </param>
    public static ApiTestHost Compose(
        Action<WebApplication>? mapProbes = null, bool withIdentityDatabase = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=wm_never_opened",
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SigningKey"] = SigningKey,
        });
        builder.WebHost.UseTestServer();

        foreach (var module in Modules())
            module.RegisterServices(builder.Services, builder.Configuration);

        builder.Services.AddSignalR();
        builder.Services.AddHealthChecks();
        // Mirrors Program.cs. It is here because CORS ordering is part of what the fallback
        // policy can break: a browser preflight carries no Authorization header, so if the CORS
        // middleware stopped short-circuiting it, default-deny would turn every cross-origin
        // call from the portal into a 401.
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(SpaOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        SqliteConnection? identityDb = null;
        if (withIdentityDatabase)
        {
            // Held open for the life of the host: an in-memory SQLite database exists only while
            // a connection to it does.
            identityDb = new SqliteConnection("Filename=:memory:");
            identityDb.Open();

            // Every descriptor the module's AddDbContext left behind, or EF refuses two providers
            // for one context.
            foreach (var descriptor in builder.Services
                .Where(d => d.ServiceType.FullName?.Contains("DbContextOptions", StringComparison.Ordinal) == true
                            && d.ServiceType.GenericTypeArguments.Contains(typeof(IdentityDbContext)))
                .ToArray())
                builder.Services.Remove(descriptor);

            builder.Services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(identityDb));
        }

        var app = builder.Build();

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        foreach (var module in Modules())
            module.MapEndpoints(app);
        app.MapPlatformEndpoints();

        mapProbes?.Invoke(app);

        if (withIdentityDatabase)
            SeedIdentity(app);

        return new ApiTestHost(app, identityDb);
    }

    public static async Task<ApiTestHost> StartAsync(
        Action<WebApplication>? mapProbes = null, bool withIdentityDatabase = false)
    {
        var host = Compose(mapProbes, withIdentityDatabase);
        await host._app.StartAsync();
        return host;
    }

    /// <summary>Every endpoint this host maps, hub and health check included.</summary>
    public IReadOnlyList<Endpoint> Endpoints =>
        [.. ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)];

    /// <summary>An anonymous caller: no <c>Authorization</c> header at all.</summary>
    public HttpClient Client => _app.GetTestClient();

    /// <summary>
    /// A signed-in caller. Called with no arguments it is a user holding <em>no</em> permission —
    /// authenticated and nothing more, which is exactly what the fallback policy asks for.
    /// </summary>
    public HttpClient ClientWith(params string[] permissions)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenFor(permissions));
        return client;
    }

    /// <summary>Minted by the product's own <see cref="TokenService"/>, not hand-rolled.</summary>
    public static string TokenFor(params string[] permissions)
    {
        var user = new User
        {
            UserName = "tester",
            Email = "tester@wm.local",
            DisplayName = "Tester",
        };
        return new TokenService(Options.Create(Jwt)).CreateTokenPair(user, permissions).AccessToken;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        if (_identityDb is not null)
            await _identityDb.DisposeAsync();
    }

    /// <summary>
    /// One user with one role granting one permission — enough to sign in and to prove the token
    /// that comes back carries what it should.
    /// </summary>
    private static void SeedIdentity(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Database.EnsureCreated();

        var role = new Role { Name = "Tester" };
        role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = SeededPermission });

        var user = new User
        {
            UserName = SeededUserName,
            Email = "tester@wm.local",
            DisplayName = "Tester",
        };
        user.PasswordHash = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<User>>()
            .HashPassword(user, SeededPassword);

        db.Roles.Add(role);
        db.Users.Add(user);
        db.SaveChanges();

        db.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.SaveChanges();
    }

    private static IModule[] Modules() =>
        [new IdentityModule(), new PeopleModule(), new TimeAttendanceModule()];
}
