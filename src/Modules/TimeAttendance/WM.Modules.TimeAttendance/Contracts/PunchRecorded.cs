namespace WM.Modules.TimeAttendance.Contracts;

/// <summary>
/// Integration event published to the event stream (Kafka topic wm.punches)
/// and pushed to the live dashboard over SignalR.
/// </summary>
public sealed record PunchRecorded(
    Guid PunchId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid SiteId,
    DateTimeOffset Timestamp,
    string Direction,
    string Source);
