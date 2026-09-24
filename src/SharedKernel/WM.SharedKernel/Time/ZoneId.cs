namespace WM.SharedKernel.Time;

/// <summary>
/// A geographic time zone, by its <b>IANA</b> id (<c>"Europe/Ljubljana"</c>), that is known to
/// exist on this host. There is no way to hold an invalid one: the constructor is private,
/// <see cref="Parse"/> throws, <see cref="TryParse"/> refuses, and it is a class so no
/// <c>default</c> instance can exist either (plan 008 P1).
///
/// <para>
/// <b>IANA only, on every OS.</b> Legacy stored a Windows id (<c>SoftwareMainOptions.SystemTimeZone</c>,
/// <c>docs/TLW-TIME-MODEL.md</c>); WM runs in Linux containers and talks to a Flutter client, both
/// of which speak IANA. <c>TimeZoneInfo.FindSystemTimeZoneById</c> accepts <em>either</em> kind on
/// both Windows and Linux (it converts), so "it resolved" is not enough — a Windows id would be
/// accepted on a developer's machine and stored in the form nothing downstream expects. A zone is
/// accepted only when the id that came back is an IANA id, which makes the answer the same on
/// every host.
/// </para>
/// </summary>
public sealed class ZoneId : IEquatable<ZoneId>
{
    private ZoneId(TimeZoneInfo info)
    {
        Info = info;
    }

    /// <summary>The IANA id, e.g. <c>"Asia/Tashkent"</c>.</summary>
    public string Id => Info.Id;

    /// <summary>The resolved zone rules. Use these to convert; never re-resolve from <see cref="Id"/>.</summary>
    public TimeZoneInfo Info { get; }

    public static ZoneId Utc { get; } = Parse("UTC");

    /// <exception cref="ArgumentException">The id is blank, unknown, or not an IANA id.</exception>
    public static ZoneId Parse(string? id) =>
        TryParse(id, out var zone, out var fault) ? zone : throw new ArgumentException(fault, nameof(id));

    public static bool TryParse(string? id, out ZoneId zone) => TryParse(id, out zone, out _);

    /// <param name="fault">Why the id was refused, naming it — safe to put in a 400 or a boot log.</param>
    public static bool TryParse(string? id, out ZoneId zone, out string fault)
    {
        zone = null!;
        if (string.IsNullOrWhiteSpace(id))
        {
            fault = "A time zone is required; an empty value is not a zone.";
            return false;
        }

        if (id.Trim() != id)
        {
            fault = $"'{id}' has surrounding whitespace; a time zone id must be exact.";
            return false;
        }

        TimeZoneInfo info;
        try
        {
            info = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            fault = $"'{id}' is not a time zone this host knows.";
            return false;
        }

        if (!info.HasIanaId)
        {
            fault = $"'{id}' is a Windows time zone id; use the IANA id (e.g. 'Europe/Ljubljana').";
            return false;
        }

        zone = new ZoneId(info);
        fault = string.Empty;
        return true;
    }

    /// <summary>The calendar date on the wall clock of this zone at <paramref name="instant"/>.</summary>
    public DateOnly DateAt(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Info).DateTime);

    public bool Equals(ZoneId? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ZoneId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Id;
}
