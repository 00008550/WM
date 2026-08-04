namespace WM.Modules.TimeAttendance.Contracts;

/// <summary>
/// Integration event published to the event stream (Kafka topic wm.punches)
/// and pushed to the live dashboard over SignalR.
///
/// <see cref="SiteId"/> and <see cref="DepartmentId"/> are the employee's data-scope axes at the
/// moment of the punch. The SignalR leg addresses the event by them, so a connection only
/// receives punches for employees its user's scope contains — carrying them on the event is what
/// lets that decision be made without a second lookup per punch.
/// </summary>
public sealed record PunchRecorded(
    Guid PunchId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid SiteId,
    Guid? DepartmentId,
    DateTimeOffset Timestamp,
    string Direction,
    string Source);
