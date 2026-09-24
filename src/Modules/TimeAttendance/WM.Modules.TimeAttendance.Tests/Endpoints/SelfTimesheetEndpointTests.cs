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
/// 008 P4 review: <c>GET /api/me/timesheet</c> without <c>to</c> needs the caller's home-site zone
/// for their local today. It must find the caller's <b>own</b> record whatever their data scope
/// says about other people — a Sites{A}-scoped manager filed at site B is still themselves.
/// </summary>
public sealed class SelfTimesheetEndpointTests
{
    private static readonly Guid Me = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000e");
    private static readonly Guid SiteB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Fact]
    public async Task A_caller_whose_own_site_is_outside_their_scope_still_gets_their_own_timesheet()
    {
        // 12:00 UTC on 15 Jan is the 16th in Auckland, so the default range ends on the 16th and
        // includes a punch frozen on that date.
        await using var app = await StartAsync(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), db =>
            db.Punches.Add(new Punch
            {
                EmployeeId = Me, EmployeeCode = "ME", SiteId = SiteB,
                Timestamp = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero),
                LocalDate = new DateOnly(2026, 1, 16), LocalZone = "Pacific/Auckland",
            }));

        var response = await app.GetTestClient().GetAsync("/api/me/timesheet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var days = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-01-16", Assert.Single(days.EnumerateArray().ToList()).GetProperty("date").GetString());
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

        var databaseName = $"self-timesheet-{Guid.NewGuid()}";
        builder.Services.AddDbContext<TimeAttendanceDbContext>(o => o.UseInMemoryDatabase(databaseName));
        // Out of scope: every scoped lookup misses the caller; only FindSelfAsync finds them.
        builder.Services.AddScoped<IEmployeeDirectory>(_ => new StubDirectory(
            new EmployeeSummary(Me, "ME", "Me Myself", null, SiteB, null, new DateOnly(2020, 1, 1), null, false),
            outOfScope: true));
        builder.Services.AddSingleton<ISiteTimeZones>(new FixedZones(ZoneId.Parse("Pacific/Auckland")));
        builder.Services.AddSingleton<IClock>(new WM.SharedKernel.Time.SystemClock(new FixedTime(now)));
        builder.Services.AddSingleton<IOwningDayResolver, LocalCalendarDayResolver>();
        builder.Services.AddScoped<IClockingDays, PunchBackedClockingDays>();
        builder.Services.AddSingleton<IEventStreamProducer, NoopEventStream>();
        builder.Services.AddSingleton(Options.Create(new PunchDeduplicationOptions()));
        builder.Services.AddSingleton(Options.Create(new PunchTimingOptions()));
        builder.Services.AddScoped<PunchService>();
        builder.Services.AddScoped<ICurrentUser>(_ => new Caller());

        builder.Services.AddAuthentication(SelfService.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SelfService>(SelfService.SchemeName, null);
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
        public IReadOnlySet<string> Permissions => new HashSet<string> { WmPermissions.SelfService };
        public bool HasPermission(string permission) => permission == WmPermissions.SelfService;
    }

    /// <summary>Every request is a signed-in caller holding exactly the self-service permission.</summary>
    private sealed class SelfService(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(WmPermissions.ClaimType, WmPermissions.SelfService)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
