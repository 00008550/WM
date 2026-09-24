namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// Which clock WM trusts for a punch, and when a client's clock earns a flag (plan 008 P5).
///
/// <para>
/// <b>The contract, for the Flutter client's offline queue.</b> A punch may carry the instant it
/// happened (<c>timestamp</c>). When it does, that instant — with the UTC offset the device wrote on
/// it — is what the punch <i>is</i>: it decides the punch's local day and its place in the timesheet.
/// The server's receive time is stored beside it (<c>receivedAt</c>) and never replaces it. When the
/// punch carries no timestamp, the server's clock stamps it and the two are equal.
/// </para>
///
/// <list type="bullet">
/// <item><b>Rejected (400):</b> a <c>timestamp</c> without an offset (<c>"2026-09-25T08:00:00"</c>) —
/// legacy read such a value as server-local (<c>GetTimeForSwipe</c>) and nothing afterwards could
/// say which instant was meant. Send the device's <b>local</b> offset (<c>±hh:mm</c>); <c>Z</c> is
/// accepted as an instant but is compared against the home zone like any other offset, so a device in
/// Tashkent that sends <c>Z</c> is flagged <c>OffsetMismatch</c>. Also rejected: a timestamp more
/// than five minutes after the server's clock.</item>
/// <item><b>Accepted and flagged:</b> a timestamp older than <see cref="LateAfter"/> on arrival
/// (<c>Late</c>), and an offset that contradicts the home site's zone at that instant by more than
/// the zone's own DST shift (<c>OffsetMismatch</c>). A flag never drops a punch.</item>
/// </list>
///
/// <para>Bound from <see cref="SectionName"/> and fault-checked at composition, the same shape as
/// <see cref="PunchDeduplicationOptions"/> and Identity's <c>AccountLockoutOptions</c>.</para>
/// </summary>
public sealed class PunchTimingOptions
{
    public const string SectionName = "PunchTiming";

    /// <summary>
    /// A punch whose own instant is older than this when the server receives it is flagged
    /// <c>Late</c>. Default one hour: long enough that a phone briefly out of signal is not noise,
    /// short enough that a shift queued overnight is visible. Env <c>PunchTiming__LateAfter</c>.
    /// </summary>
    public TimeSpan LateAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>One line saying why this configuration cannot be used, or <c>null</c> when it can.</summary>
    public static string? DescribeFault(PunchTimingOptions options)
    {
        if (options.LateAfter <= TimeSpan.Zero)
            return $"{SectionName}:LateAfter is {options.LateAfter}, which is not positive. Set it (env: "
                   + "PunchTiming__LateAfter) to a positive duration such as 01:00:00.";

        return null;
    }
}
