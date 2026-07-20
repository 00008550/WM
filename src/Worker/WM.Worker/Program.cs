using MassTransit;
using Serilog;
using WM.Worker.Consumers;
using WM.Worker.Kafka;
using WM.Worker.Plugins;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateLogger();
builder.Services.AddSerilog();

builder.Services.AddSingleton<PluginHost>();
builder.Services.AddHostedService<PluginBootstrapService>();
builder.Services.AddHostedService<PunchStreamConsumer>();

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<RunPayrollExportConsumer>();
    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:User"] ?? "wm");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "wm_dev_password");
        });
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
await host.RunAsync();

/// <summary>Loads plugins at startup, before any messages are consumed.</summary>
internal sealed class PluginBootstrapService(
    PluginHost pluginHost,
    IConfiguration configuration,
    IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var pluginDirectory = configuration["Plugins:Directory"]
            ?? Path.Combine(environment.ContentRootPath, "plugins");
        pluginHost.LoadFromDirectory(pluginDirectory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
