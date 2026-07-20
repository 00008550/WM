using Microsoft.Extensions.Logging;

namespace WM.Plugins.Abstractions;

/// <summary>Manifest loaded from plugin.json next to the plugin assembly.</summary>
public sealed record PluginManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Type { get; init; } // payroll-export | connector | report | notification-channel
    public string? EntryAssembly { get; init; }
    /// <summary>License feature required to load this plugin, e.g. "plugin:payroll.demo".</summary>
    public string? RequiredLicenseFeature { get; init; }
    public string Description { get; init; } = string.Empty;
}

public interface IWmPlugin
{
    string Id { get; }
}

/// <summary>One calculated timesheet line handed to payroll-export plugins.</summary>
public sealed record TimesheetLine(
    string EmployeeCode,
    string EmployeeName,
    DateOnly Date,
    double RegularHours,
    double OvertimeHours,
    double AbsenceHours,
    string? AbsenceCode);

public sealed record PayrollExportContext(
    string CustomerName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<TimesheetLine> Lines,
    IReadOnlyDictionary<string, string> Settings,
    ILogger Logger);

public sealed record PayrollExportFile(string FileName, byte[] Content, string ContentType);

public sealed record PayrollExportResult(bool Succeeded, IReadOnlyList<PayrollExportFile> Files, string? Error)
{
    public static PayrollExportResult Success(params PayrollExportFile[] files) => new(true, files, null);
    public static PayrollExportResult Failure(string error) => new(false, [], error);
}

/// <summary>Transforms calculated timesheets into a payroll system's format.</summary>
public interface IPayrollExportPlugin : IWmPlugin
{
    Task<PayrollExportResult> ExportAsync(PayrollExportContext context, CancellationToken ct = default);
}

/// <summary>Scheduled two-way sync with an external HR/scheduling system.</summary>
public interface IConnectorPlugin : IWmPlugin
{
    Task SyncAsync(ConnectorContext context, CancellationToken ct = default);
}

public sealed record ConnectorContext(
    IReadOnlyDictionary<string, string> Settings,
    ILogger Logger);

/// <summary>Delivers notifications over a channel (mail, chat, webhook, push…).</summary>
public interface INotificationChannelPlugin : IWmPlugin
{
    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}

public sealed record NotificationMessage(
    string Recipient,
    string Subject,
    string Body,
    IReadOnlyDictionary<string, string> Metadata);
