using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Events;
using WM.SharedKernel.Security;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Modules.TimeAttendance.Tests.Endpoints;

/// <summary>
/// 008 P5, on the wire: <c>POST /api/punches</c> is the Flutter offline queue's contract. A
/// timestamp without an offset is a 400 that says why; one with an offset is a 201 whose body
/// carries both clocks and the flags.
/// </summary>
public sealed class PunchTimestampEndpointTests
{
    private static readonly Guid Me = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");
    private static readonly Guid SiteB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Fact]
    public async Task An_offset_less_timestamp_is_a_400_that_names_the_offset()
    {
        await using var app = await StartAsync(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), _ => { });

        var response = await app.GetTestClient().PostAsJsonAsync("/api/punches",
            new { employeeCode = "ME", direction = 0, source = 2, timestamp = "2026-01-15T09:00:00" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("offset", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_queued_timestamp_with_an_offset_is_a_201_carrying_both_clocks_and_the_flag()
    {
        await using var app = await StartAsync(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), _ => { });

        var response = await app.GetTestClient().PostAsJsonAsync("/api/punches",
            new { employeeCode = "ME", direction = 0, source = 2, timestamp = "2026-01-14T08:00:00+13:00" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var punch = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new DateTimeOffset(2026, 1, 13, 19, 0, 0, TimeSpan.Zero), punch.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), punch.GetProperty("receivedAt").GetDateTimeOffset());
        Assert.Equal(780, punch.GetProperty("clientUtcOffsetMinutes").GetInt32());
        Assert.Equal((int)PunchFlags.Late, punch.GetProperty("flags").GetInt32());
        Assert.Equal("2026-01-14", punch.GetProperty("localDate").GetString());
    }

    private static async Task<WebApplication> StartAsync(DateTimeOffset now, Action<TimeAttendanceDbContext> seed)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        var databaseName = $"punch-timestamp-{Guid.NewGuid()}";
        builder.Services.AddDbContext<TimeAttendanceDbContext>(o => o.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<IEmployeeDirectory>(_ => new StubDirectory(
            new EmployeeSummary(Me, "ME", "Me Myself", null, SiteB, null, new DateOnly(2020, 1, 1), null, false)));
        builder.Services.AddSingleton<ISiteTimeZones>(new FixedZones(ZoneId.Parse("Pacific/Auckland")));
        builder.Services.AddSingleton<IClock>(new WM.SharedKernel.Time.SystemClock(new FixedTime(now)));
        builder.Services.AddSingleton<IOwningDayResolver, LocalCalendarDayResolver>();
        builder.Services.AddScoped<IClockingDays, PunchBackedClockingDays>();
        builder.Services.AddSingleton<IEventStreamProducer, NoopEventStream>();
        builder.Services.AddSingleton(Options.Create(new PunchDeduplicationOptions()));
        builder.Services.AddSingleton(Options.Create(new PunchTimingOptions()));
        builder.Services.AddScoped<PunchService>();
        builder.Services.AddScoped<ICurrentUser>(_ => new Caller());

        builder.Services.AddAuthentication(Recorder.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, Recorder>(Recorder.SchemeName, null);
        builder.Services.AddAuthorization(o =>
        {
            foreach (var permission in WmPermissions.All)
                o.AddPolicy(permission, p => p.RequireClaim(WmPermissions.ClaimType, permission));
        });

        var app = builder.Build();
        new TimeAttendanceModule().MapEndpoints(app);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeAttendanceDbContext>();
            seed(db);
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        return app;
    }

    private sealed class Caller : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserName => "me";
        public Guid? EmployeeId => Me;
        public bool IsAuthenticated => true;
        public IReadOnlySet<string> Permissions => new HashSet<string> { WmPermissions.PunchesRecord };
        public bool HasPermission(string permission) => permission == WmPermissions.PunchesRecord;
    }

    /// <summary>Every request is a signed-in caller holding exactly the punch-recording permission.</summary>
    private sealed class Recorder(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(WmPermissions.ClaimType, WmPermissions.PunchesRecord)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
