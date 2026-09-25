using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using WM.Api.Infrastructure;
using WM.Api.Realtime;
using WM.Modules.Identity;
using WM.Modules.Identity.Data;
using WM.Modules.People;
using WM.Modules.People.Data;
using WM.Modules.TimeAttendance;
using WM.Modules.TimeAttendance.Data;
using WM.SharedKernel.Events;
using WM.SharedKernel.Modules;
using WM.SharedKernel.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// The one clock, and the installation's zone — refused at composition if missing or unknown
// (plan 008 P1). Before the modules, so any of them may depend on either.
builder.Services.AddWmClock(builder.Configuration);

IModule[] modules =
[
    new IdentityModule(),
    new PeopleModule(),
    new TimeAttendanceModule(),
];

foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);

builder.Services.AddScoped<WM.Api.Infrastructure.DemoUserSeeder>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<AttendanceConnectionRegistry>();
builder.Services.AddSingleton<AttendanceAudience>();
// This host is the one that pushes live data, so it is the one that has to act on a scope
// change. Replace rather than Add: the Identity module registers a no-op default so it works in
// hosts with no live transport, and two registrations of the same interface would leave which
// one wins depending on registration order.
builder.Services.Replace(ServiceDescriptor.Singleton<IScopeChangeNotifier>(
    sp => sp.GetRequiredService<AttendanceAudience>()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Liveness plus the readiness checks behind /api/health/ready — see WmHealthChecks for why the
// two are separate and why readiness sits under the prefix nginx proxies.
builder.Services.AddWmHealthChecks();

// Real client addresses out of the proxy chain, and a bound on what one of them may spend
// unauthenticated. See PublicEdge for why the two are configured together.
builder.Services.AddWmPublicEdge(builder.Configuration);

// Event stream: Kafka when configured, and always fan out to SignalR for live UI.
builder.Services.AddSingleton<KafkaEventStreamProducer>();
builder.Services.AddScoped<IEventStreamProducer, BroadcastingEventStreamProducer>();

const string CorsPolicy = "spa";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Outermost, so a request the edge refuses is still logged: a 429 nobody can see in the log is a
// support call nobody can answer.
app.UseSerilogRequestLogging();

// Forwarded headers → CORS → rate limiter, then authentication. PublicEdge holds the reason for
// each step of that order, and the test host calls the same method.
app.UseWmPublicEdge(CorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

foreach (var module in modules)
    module.MapEndpoints(app);

// The hub, /health and /api/health/ready — see PlatformEndpoints for why they are not inline here.
app.MapPlatformEndpoints();

// Bootstrap. Schema migration runs in every environment — on-prem installs
// upgrade themselves on start, which is how a customer's server stays current
// without us reaching it. Demo data is Development-only.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<PeopleDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<TimeAttendanceDbContext>().Database.MigrateAsync();
    // 008 P4: punches from before the local day was frozen on the row get one, through People's zone
    // contract — the migration cannot resolve zones without reading People's schema.
    await services.GetRequiredService<PunchLocalDateBackfill>().RunAsync();
    // 002 P1: after the punches have their day, each such day gets its Clocking row. Idempotent and
    // safe with several instances starting at once (insert-if-absent on the unique key).
    await services.GetRequiredService<ClockingBackfill>().RunAsync();

    if (app.Environment.IsDevelopment())
    {
        await services.GetRequiredService<IdentitySeeder>().SeedAsync();
        await services.GetRequiredService<PeopleSeeder>().SeedAsync();
        await services.GetRequiredService<PunchSeeder>().SeedAsync();
        await services.GetRequiredService<DemoUserSeeder>().SeedAsync();
    }
    else
    {
        // A fresh production install still needs one way in.
        await services.GetRequiredService<IdentitySeeder>().SeedAsync();
    }
}

app.Run();
