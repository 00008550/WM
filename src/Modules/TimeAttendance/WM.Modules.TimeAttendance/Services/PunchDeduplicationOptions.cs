namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// How close together two same-direction punches have to be before the second is treated as a
/// double-click / double-swipe rather than a real event.
///
/// <para>
/// <b>This window is a WM decision, not a legacy port.</b> No minimum-interval setting exists in
/// TLW: legacy relied on the physical terminal to swallow a double swipe, and the only anti-passback
/// configuration in <c>E:\Tlw</c> is Salto/Suprema <i>device</i> config — dropped hardware (§14
/// decision 3). WM's terminal is a web page, so <c>PunchService</c> has to do it. The default is
/// <b>60 seconds</b>, chosen to cover a fumbled tap or a page double-submit without swallowing two
/// deliberate punches; it is bound from configuration so an install can tune it
/// (<see cref="SectionName"/>, env <c>PunchDeduplication__Window</c>).
/// </para>
///
/// <para>
/// A genuine unpaired run minutes or hours apart (IN, IN, IN) is <b>not</b> deduplicated here — that
/// is a day-level exception that plan 002 owns. P2 suppresses only the seconds-apart noise so 002
/// inherits clean data.
/// </para>
/// </summary>
public sealed class PunchDeduplicationOptions
{
    public const string SectionName = "PunchDeduplication";

    /// <summary>
    /// A same-direction punch within this window of an existing one is accepted idempotently: the
    /// existing punch is returned and no second row is created.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One line saying why this configuration cannot be used, or <c>null</c> when it can — the same
    /// shape and the same composition-time check as <c>AccountLockoutOptions.DescribeFault</c>. A
    /// negative window is nonsense; a zero window turns the guard off, which is a legitimate choice
    /// and left to the operator.
    /// </summary>
    public static string? DescribeFault(PunchDeduplicationOptions options)
    {
        if (options.Window < TimeSpan.Zero)
            return $"{SectionName}:Window is {options.Window}, which is negative. Set it (env: "
                   + "PunchDeduplication__Window) to a non-negative duration such as 00:01:00, or "
                   + "00:00:00 to turn same-direction deduplication off.";

        return null;
    }
}
