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

/// <summary>
/// Why a recorded punch deserves a second look (008 P5). A flag never refuses a punch — offline
/// queues are the point — it makes the anomaly visible after the fact instead of inferred.
/// </summary>
[Flags]
public enum PunchFlags
{
    None = 0,

    /// <summary>The client's instant is older than <c>PunchTiming:LateAfter</c> when the server
    /// received it: a queued offline punch, or a device clock running behind.</summary>
    Late = 1,

    /// <summary>The client's UTC offset disagrees with the home site's resolved zone at that instant
    /// by more than the zone's own DST shift: the device's zone setting is wrong, or it is elsewhere.</summary>
    OffsetMismatch = 2,
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

    /// <summary>
    /// The working day this punch belongs to, resolved once when it was recorded and <b>never
    /// re-derived</b> (plan 008 P4, open question 4 = freeze): editing the site's zone, or a tzdata
    /// update, does not move a recorded punch to another day. <see cref="Timestamp"/> stays the
    /// authoritative instant.
    /// <para>Nullable only because the migration that added it could not resolve zones — they live
    /// in People (invariant 1). <c>PunchLocalDateBackfill</c> fills every such row at startup before
    /// the host serves a request, and every new punch is written with it set.</para>
    /// </summary>
    public DateOnly? LocalDate { get; set; }

    /// <summary>The IANA zone <see cref="LocalDate"/> was resolved in — the employee's home-site zone
    /// at record time — kept so the frozen answer can be explained later.</summary>
    public string? LocalZone { get; set; }

    /// <summary>
    /// When the server received this punch (008 P5). <see cref="Timestamp"/> is the instant the punch
    /// claims — the client's, when it supplied one; this is the server's own clock, so skew and queue
    /// age are recorded rather than inferred. Equal to <see cref="Timestamp"/> for a server-stamped punch.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>The UTC offset the client wrote on its timestamp, in minutes; <c>null</c> when the
    /// server stamped the punch. Kept because <c>timestamptz</c> stores the instant only.</summary>
    public int? ClientUtcOffsetMinutes { get; set; }

    public PunchFlags Flags { get; set; }
}
