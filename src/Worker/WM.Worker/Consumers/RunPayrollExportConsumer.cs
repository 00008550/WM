using MassTransit;
using WM.Plugins.Abstractions;
using WM.Worker.Contracts;
using WM.Worker.Plugins;

namespace WM.Worker.Consumers;

public sealed class RunPayrollExportConsumer(
    PluginHost pluginHost,
    ILogger<RunPayrollExportConsumer> logger) : IConsumer<RunPayrollExport>
{
    public async Task Consume(ConsumeContext<RunPayrollExport> context)
    {
        var command = context.Message;
        var plugin = pluginHost.GetPlugins<IPayrollExportPlugin>()
            .FirstOrDefault(p => p.Id.Equals(command.PluginId, StringComparison.OrdinalIgnoreCase));

        if (plugin is null)
        {
            logger.LogError("Payroll plugin {PluginId} is not installed", command.PluginId);
            await context.Publish(new PayrollExportCompleted(
                command.RequestId, command.PluginId, false, [], "Plugin not installed."));
            return;
        }

        // TODO(phase-2): pull real calculated timesheets from the Rules module.
        var lines = DemoTimesheetData.ForPeriod(command.PeriodStart, command.PeriodEnd);

        var result = await plugin.ExportAsync(new PayrollExportContext(
            command.CustomerName, command.PeriodStart, command.PeriodEnd,
            lines, command.Settings, logger), context.CancellationToken);

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "exports", command.RequestId.ToString("N"));
        if (result.Succeeded)
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (var file in result.Files)
                await File.WriteAllBytesAsync(Path.Combine(outputDirectory, file.FileName), file.Content, context.CancellationToken);
            logger.LogInformation("Payroll export {RequestId} via {PluginId}: {Count} file(s) written to {Dir}",
                command.RequestId, plugin.Id, result.Files.Count, outputDirectory);
        }
        else
        {
            logger.LogError("Payroll export {RequestId} via {PluginId} failed: {Error}",
                command.RequestId, plugin.Id, result.Error);
        }

        await context.Publish(new PayrollExportCompleted(
            command.RequestId, command.PluginId, result.Succeeded,
            result.Files.Select(f => f.FileName).ToArray(), result.Error));
    }
}

internal static class DemoTimesheetData
{
    public static IReadOnlyList<TimesheetLine> ForPeriod(DateOnly start, DateOnly end)
    {
        var rng = new Random(42);
        var employees = new[] { ("E1000", "Ava Kovač"), ("E1001", "Liam Novak"), ("E1002", "Maja Weber") };
        var lines = new List<TimesheetLine>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            foreach (var (code, name) in employees)
                lines.Add(new TimesheetLine(code, name, date, 8, rng.NextDouble() < 0.2 ? 1.5 : 0, 0, null));
        }
        return lines;
    }
}
