using Microsoft.EntityFrameworkCore;
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

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

IModule[] modules =
[
    new IdentityModule(),
    new PeopleModule(),
    new TimeAttendanceModule(),
];

foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

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

app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

foreach (var module in modules)
    module.MapEndpoints(app);

app.MapHub<AttendanceHub>("/hubs/attendance");
app.MapHealthChecks("/health");

// Development bootstrap: create schema + seed demo data.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<PeopleDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<TimeAttendanceDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<IdentitySeeder>().SeedAsync();
    await services.GetRequiredService<PeopleSeeder>().SeedAsync();
    await services.GetRequiredService<PunchSeeder>().SeedAsync();
}

app.Run();
