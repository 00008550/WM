namespace WM.Worker.Contracts;

/// <summary>Command sent over RabbitMQ: run a payroll export with the given plugin.</summary>
public sealed record RunPayrollExport(
    Guid RequestId,
    string PluginId,
    string CustomerName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Dictionary<string, string> Settings);

public sealed record PayrollExportCompleted(
    Guid RequestId,
    string PluginId,
    bool Succeeded,
    string[] FileNames,
    string? Error);
