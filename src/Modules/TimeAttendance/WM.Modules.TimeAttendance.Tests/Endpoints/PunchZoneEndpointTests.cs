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
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Events;
using WM.SharedKernel.Security;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Modules.TimeAttendance.Tests.Endpoints;

/// <summary>
/// 022 P1: every punch payload says which clock and which day. The zone is the one frozen on the
/// punch (008 P4), falling back to the site's zone only for a row that has none; the day is the
/// frozen <c>LocalDate</c>, never recomputed from the instant.
/// </summary>
public sealed class PunchZoneEndpointTests
{
    private static readonly Guid Emp = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000022");
    private static readonly Guid Site = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000022");
    private const string Ljubljana = "Europe/Ljubljana";

    // The observed case (plan 022, Edge cases): 19:44 UTC is 21:44 in Ljubljana on 17 Sep.
    private static readonly DateTimeOffset Observed = new(2026, 9, 17, 19, 44, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

    private static Punch ObservedPunch(string? zone = Ljubljana) => new()
    {
        EmployeeId = Emp, EmployeeCode = "E22", SiteId = Site, Direction = PunchDirection.In,
        Timestamp = Observed, ReceivedAt = Observed,
        LocalDate = new DateOnly(2026, 9, 17), LocalZone = zone,
    };

    [Fact]
    public async Task The_observed_Ljubljana_punch_carries_its_zone_and_day_on_every_read()
    {
        await using var app = await StartAsync(Ljubljana, db => db.Punches.Add(ObservedPunch()));
        var client = app.GetTestClient();

        var recent = Single(await client.GetFromJsonAsync<JsonElement>("/api/punches/recent"));
        Assert.Equal(Ljubljana, recent.GetProperty("localZone").GetString());
        Assert.Equal("2026-09-17", recent.GetProperty("localDate").GetString());

        var mine = Single(await client.GetFromJsonAsync<JsonElement>("/api/me/punches"));
        Assert.Equal(Ljubljana, mine.GetProperty("localZone").GetString());
        Assert.Equal("2026-09-17", mine.GetProperty("localDate").GetString());

        var live = Single((await client.GetFromJsonAsync<JsonElement>("/api/attendance/live")).GetProperty("present"));
        Assert.Equal(Ljubljana, live.GetProperty("sinceLocalZone").GetString());
        Assert.Equal("2026-09-17", live.GetProperty("sinceLocalDate").GetString());
    }

    [Fact]
    public async Task A_site_zone_changed_after_the_punch_does_not_move_its_clock()
    {
        // The site now resolves to Tashkent; the punch was recorded on Ljubljana's clock.
        await using var app = await StartAsync("Asia/Tashkent", db => db.Punches.Add(ObservedPunch()));
        var client = app.GetTestClient();

        Assert.Equal(Ljubljana, Single(await client.GetFromJsonAsync<JsonElement>("/api/punches/recent"))
            .GetProperty("localZone").GetString());
        Assert.Equal(Ljubljana, Single(await client.GetFromJsonAsync<JsonElement>("/api/me/punches"))
            .GetProperty("localZone").GetString());
        Assert.Equal(Ljubljana, Single((await client.GetFromJsonAsync<JsonElement>("/api/attendance/live"))
            .GetProperty("present")).GetProperty("sinceLocalZone").GetString());
        var day = Single(await client.GetFromJsonAsync<JsonElement>(
            $"/api/attendance/timesheet/{Emp}?from=2026-09-17&to=2026-09-17"));
        Assert.Equal(Ljubljana, Single(day.GetProperty("intervals")).GetProperty("inZone").GetString());
    }

    [Fact]
    public async Task A_punch_without_a_frozen_zone_falls_back_to_its_site_zone()
    {
        await using var app = await StartAsync(Ljubljana, db => db.Punches.Add(ObservedPunch(zone: null)));
        var client = app.GetTestClient();

        Assert.Equal(Ljubljana, Single(await client.GetFromJsonAsync<JsonElement>("/api/punches/recent"))
            .GetProperty("localZone").GetString());
        Assert.Equal(Ljubljana, Single((await client.GetFromJsonAsync<JsonElement>("/api/attendance/live"))
            .GetProperty("present")).GetProperty("sinceLocalZone").GetString());
        var day = Single(await client.GetFromJsonAsync<JsonElement>("/api/me/timesheet?from=2026-09-17&to=2026-09-17"));
        Assert.Equal(Ljubljana, Single(day.GetProperty("intervals")).GetProperty("inZone").GetString());
    }

    [Fact]
    public async Task The_day_is_the_frozen_one_not_recomputed_from_the_instant()
    {
        // A night-shift Out at 02:30 Ljubljana on the 18th that 010 P2 filed under the 17th.
        await using var app = await StartAsync(Ljubljana, db => db.Punches.Add(new Punch
        {
            EmployeeId = Emp, EmployeeCode = "E22", SiteId = Site, Direction = PunchDirection.In,
            Timestamp = new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero),
            ReceivedAt = new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero),
            LocalDate = new DateOnly(2026, 9, 17), LocalZone = Ljubljana,
        }), now: new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero));
        var client = app.GetTestClient();

        Assert.Equal("2026-09-17", Single(await client.GetFromJsonAsync<JsonElement>("/api/punches/recent"))
            .GetProperty("localDate").GetString());
        Assert.Equal("2026-09-17", Single(await client.GetFromJsonAsync<JsonElement>("/api/me/punches"))
            .GetProperty("localDate").GetString());
        Assert.Equal("2026-09-17", Single((await client.GetFromJsonAsync<JsonElement>("/api/attendance/live"))
            .GetProperty("present")).GetProperty("sinceLocalDate").GetString());
    }

    [Fact]
    public async Task Timesheet_intervals_carry_a_zone_per_end_and_none_for_a_missing_out()
    {
        await using var app = await StartAsync("Asia/Tashkent", db =>
        {
            db.Punches.Add(new Punch
            {
                EmployeeId = Emp, EmployeeCode = "E22", SiteId = Site, Direction = PunchDirection.In,
                Timestamp = Observed.AddHours(-10), LocalDate = new DateOnly(2026, 9, 17), LocalZone = Ljubljana,
            });
            // The Out was recorded after the site's zone was edited: it keeps its own clock.
            db.Punches.Add(new Punch
            {
                EmployeeId = Emp, EmployeeCode = "E22", SiteId = Site, Direction = PunchDirection.Out,
                Timestamp = Observed.AddHours(-2), LocalDate = new DateOnly(2026, 9, 17), LocalZone = "Europe/Vienna",
            });
            db.Punches.Add(ObservedPunch()); // open In
        });

        var day = Single(await app.GetTestClient().GetFromJsonAsync<JsonElement>(
            $"/api/attendance/timesheet/{Emp}?from=2026-09-17&to=2026-09-17"));
        var intervals = day.GetProperty("intervals").EnumerateArray().ToList();

        Assert.Equal(2, intervals.Count);
        Assert.Equal(Ljubljana, intervals[0].GetProperty("inZone").GetString());
        Assert.Equal("Europe/Vienna", intervals[0].GetProperty("outZone").GetString());
        Assert.Equal(Ljubljana, intervals[1].GetProperty("inZone").GetString());
        Assert.Equal(JsonValueKind.Null, intervals[1].GetProperty("outZone").ValueKind);
    }

    [Fact]
    public async Task The_published_PunchRecorded_carries_the_frozen_day_and_zone()
    {
        var stream = new CapturingEventStream();
        await using var app = await StartAsync(Ljubljana, _ => { }, stream: stream);

        var response = await app.GetTestClient().PostAsJsonAsync("/api/punches",
            new { employeeCode = "E22", direction = "In", timestamp = "2026-09-17T21:44:00+02:00" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var published = Assert.IsType<PunchRecorded>(Assert.Single(stream.Events));
        Assert.Equal(Ljubljana, published.LocalZone);
        Assert.Equal(new DateOnly(2026, 9, 17), published.LocalDate);
    }

    private static JsonElement Single(JsonElement array) => Assert.Single(array.EnumerateArray().ToList());

    private static async Task<WebApplication> StartAsync(
        string siteZone, Action<TimeAttendanceDbContext> seed,
        DateTimeOffset? now = null, IEventStreamProducer? stream = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        var databaseName = $"punch-zone-{Guid.NewGuid()}";
        builder.Services.AddDbContext<TimeAttendanceDbContext>(o => o.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<IEmployeeDirectory>(_ => new StubDirectory(
            new EmployeeSummary(Emp, "E22", "Ana Novak", null, Site, null, new DateOnly(2020, 1, 1), null, false)));
        builder.Services.AddSingleton<ISiteTimeZones>(new FixedZones(ZoneId.Parse(siteZone)));
        builder.Services.AddSingleton<IClock>(new WM.SharedKernel.Time.SystemClock(new FixedTime(now ?? Now)));
        builder.Services.AddSingleton<IOwningDayResolver, LocalCalendarDayResolver>();
        builder.Services.AddScoped<IClockingDays, PunchBackedClockingDays>();
        builder.Services.AddSingleton(stream ?? new NoopEventStream());
        builder.Services.AddSingleton(Options.Create(new PunchDeduplicationOptions()));
        builder.Services.AddSingleton(Options.Create(new PunchTimingOptions()));
        builder.Services.AddScoped<PunchService>();
        builder.Services.AddScoped<ICurrentUser>(_ => new Caller());

        builder.Services.AddAuthentication(AllPermissions.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AllPermissions>(AllPermissions.SchemeName, null);
        builder.Services.AddAuthorization(o =>
        {
            foreach (var permission in WmPermissions.All)
                o.AddPolicy(permission, p => p.RequireClaim(WmPermissions.ClaimType, permission));
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

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

    private sealed class CapturingEventStream : IEventStreamProducer
    {
        public List<object> Events { get; } = [];

        public Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
            where TEvent : class
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class Caller : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserName => "admin";
        public Guid? EmployeeId => Emp;
        public bool IsAuthenticated => true;
        public IReadOnlySet<string> Permissions => WmPermissions.All.ToHashSet();
        public bool HasPermission(string permission) => true;
    }

    /// <summary>Every request is a signed-in caller holding every permission.</summary>
    private sealed class AllPermissions(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                WmPermissions.All.Select(p => new Claim(WmPermissions.ClaimType, p)), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
