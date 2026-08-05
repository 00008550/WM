using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WM.Api.Infrastructure;
using WM.Api.Realtime;
using WM.Modules.Identity;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using WM.Modules.People;
using WM.Modules.People.Data;
using WM.Modules.TimeAttendance;
using WM.Modules.TimeAttendance.Data;
using WM.SharedKernel.Events;
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
/// <c>withDatabases</c> is the deliberate exception, for the sign-in path and for readiness.
/// </summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    public const string Issuer = "wm-tests";
    public const string Audience = "wm-tests";
    public const string SigningKey = "wm-test-signing-key-of-at-least-32-bytes";
    public const string SpaOrigin = "http://localhost:4200";

    // The seeded account, present only when the host is composed with databases.
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
    private readonly IReadOnlyList<SqliteConnection> _databases;

    private ApiTestHost(WebApplication app, IReadOnlyList<SqliteConnection> databases)
    {
        _app = app;
        _databases = databases;
    }

    /// <summary>Built and mapped, but not started — enough to enumerate what got mapped.</summary>
    /// <param name="mapProbes">Extra endpoints, mapped after the real ones.</param>
    /// <param name="withDatabases">
    /// Give every module a real (SQLite, in-process) database — the Identity one also created and
    /// carrying a seeded user, so sign-in can be exercised end to end. Off by default: the other
    /// tests are stronger for having no database at all, since a request that reaches a handler
    /// then fails loudly.
    /// </param>
    /// <param name="environmentName">
    /// Production unless a test says otherwise. Composed rather than hard-coded because 006 P2's
    /// signing-key guard is the one thing in the host that reads the environment name.
    /// </param>
    /// <param name="configurationOverrides">
    /// Applied over the defaults below. A <c>null</c> value <em>removes</em> the key, which is how
    /// "this setting was never configured" is expressed — shadowing it with an empty string would
    /// be a different fault.
    /// </param>
    public static ApiTestHost Compose(
        Action<WebApplication>? mapProbes = null,
        bool withDatabases = false,
        string? environmentName = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName ?? Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=wm_never_opened",
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SigningKey"] = SigningKey,
            // WM.Api's own appsettings.json sits next to the test assembly and the host reads it,
            // so this has to be shadowed rather than merely left unset: KafkaEventStreamProducer
            // builds a real librdkafka producer for any non-blank value, and no test may open a
            // socket. Blank is the documented "disabled" value, not an accident.
            ["Kafka:BootstrapServers"] = string.Empty,
        };
        foreach (var (key, value) in configurationOverrides ?? new Dictionary<string, string?>())
        {
            if (value is null)
                settings.Remove(key);
            else
                settings[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();

        foreach (var module in Modules())
            module.RegisterServices(builder.Services, builder.Configuration);

        builder.Services.AddSignalR();
        builder.Services.AddWmHealthChecks();

        // The rest of what Program.cs registers for itself. It is here because composing in
        // Development turns on ValidateOnBuild, and a host that cannot construct its own graph is
        // a real failure — but it can only be seen if the graph under test is the whole one.
        // KafkaEventStreamProducer is a singleton and stays unconstructed unless something
        // resolves it; the blank bootstrap-servers setting above is what makes that harmless.
        builder.Services.AddSingleton<AttendanceConnectionRegistry>();
        builder.Services.AddSingleton<AttendanceAudience>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IScopeChangeNotifier>(
            sp => sp.GetRequiredService<AttendanceAudience>()));
        builder.Services.AddSingleton<KafkaEventStreamProducer>();
        builder.Services.AddScoped<IEventStreamProducer, BroadcastingEventStreamProducer>();

        // Mirrors Program.cs. It is here because CORS ordering is part of what the fallback
        // policy can break: a browser preflight carries no Authorization header, so if the CORS
        // middleware stopped short-circuiting it, default-deny would turn every cross-origin
        // call from the portal into a 401.
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(SpaOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        List<SqliteConnection> databases = [];
        if (withDatabases)
        {
            // All three, not only Identity: readiness reports every module's database, so a host
            // with one of the three still pointing at the unreachable Postgres would never be
            // ready and the test would prove the wrong thing.
            UseSqlite<IdentityDbContext>(builder, databases);
            UseSqlite<PeopleDbContext>(builder, databases);
            UseSqlite<TimeAttendanceDbContext>(builder, databases);
        }

        var app = builder.Build();

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        foreach (var module in Modules())
            module.MapEndpoints(app);
        app.MapPlatformEndpoints();

        mapProbes?.Invoke(app);

        if (withDatabases)
        {
            SeedIdentity(app);
            MarkAsMigrated<IdentityDbContext>(app);
            MarkAsMigrated<PeopleDbContext>(app);
            MarkAsMigrated<TimeAttendanceDbContext>(app);
        }

        return new ApiTestHost(app, databases);
    }

    public static Task<ApiTestHost> StartAsync(
        Action<WebApplication>? mapProbes = null,
        bool withDatabases = false,
        string? environmentName = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null) =>
        StartAsync(Compose(mapProbes, withDatabases, environmentName, configurationOverrides));

    private static async Task<ApiTestHost> StartAsync(ApiTestHost host)
    {
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
        foreach (var database in _databases)
            await database.DisposeAsync();
    }

    /// <summary>
    /// Point one module's context at its own in-process SQLite database instead of the Postgres
    /// connection string the module registered.
    /// </summary>
    private static void UseSqlite<TContext>(
        WebApplicationBuilder builder, List<SqliteConnection> databases)
        where TContext : DbContext
    {
        // Held open for the life of the host: an in-memory SQLite database exists only while a
        // connection to it does.
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        databases.Add(connection);

        // Every descriptor the module's AddDbContext left behind, or EF refuses two providers for
        // one context.
        foreach (var descriptor in builder.Services
            .Where(d => d.ServiceType.FullName?.Contains("DbContextOptions", StringComparison.Ordinal) == true
                        && d.ServiceType.GenericTypeArguments.Contains(typeof(TContext)))
            .ToArray())
            builder.Services.Remove(descriptor);

        builder.Services.AddDbContext<TContext>(o => o.UseSqlite(connection));
    }

    /// <summary>
    /// Write the migration history a migrated database would have, using EF's own history
    /// repository so the table is whatever this provider calls it.
    ///
    /// <para>
    /// The migrations themselves cannot be replayed here: <c>AddComposedScopeConstraints</c> and
    /// friends carry schema-qualified Postgres SQL, which SQLite has no notion of. Stamping the
    /// history is the honest substitute — <c>DatabaseReadinessCheck</c> asks "is anything still
    /// pending", and that question is answered by the history table, not by the tables themselves.
    /// </para>
    /// </summary>
    private static void MarkAsMigrated<TContext>(WebApplication app)
        where TContext : DbContext
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var history = db.GetService<IHistoryRepository>();

        db.Database.ExecuteSqlRaw(history.GetCreateScript());
        foreach (var migration in db.Database.GetMigrations())
            db.Database.ExecuteSqlRaw(history.GetInsertScript(new HistoryRow(migration, "test")));
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
