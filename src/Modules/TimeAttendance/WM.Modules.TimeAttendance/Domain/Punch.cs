using WM.SharedKernel.Domain;

namespace WM.Modules.TimeAttendance.Domain;

public enum PunchDirection
{
    In = 0,
    Out = 1,
}

public enum PunchSource
{
    Terminal = 0,
    Web = 1,
    Mobile = 2,
    Import = 3,
}

/// <summary>A raw clock-in/out event — the atom of the whole platform.</summary>
public sealed class Punch : Entity
{
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public PunchDirection Direction { get; set; }
    public PunchSource Source { get; set; }
    public string? DeviceId { get; set; }
    public Guid SiteId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Guid? RecordedByUserId { get; set; }
}
