namespace WM.SharedKernel.Time;

/// <summary>
/// The only sanctioned way to read "now" in WM (plan 008 P1). A raw read of <c>DateTime</c>'s or
/// <c>DateTimeOffset</c>'s static now/today properties is refused by
/// <c>RawClockReadInventoryTests</c> unless its file is named on that test's allow-list.
///
/// <para>
/// Why one abstraction: legacy read the clock 903 times in <c>Logic</c> and <c>WebSite</c> plus
/// 401 <c>GETDATE()</c> calls, each deciding for itself whose midnight "today" meant, and its own
/// newest component ended up banning the pattern with a compile-time analyzer
/// (<c>AvoidDirectTimeUsageAnalyzer.cs</c>). An instant is always UTC here; a <em>date</em> only
/// exists in a zone, so <see cref="TodayIn"/> takes one and there is deliberately no zone-less
/// <c>Today</c>.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's calendar date on the wall clock of <paramref name="zone"/>.</summary>
    DateOnly TodayIn(ZoneId zone);
}

/// <summary>
/// <see cref="IClock"/> over <see cref="TimeProvider"/>, so a test substitutes the provider (for
/// example <c>FakeTimeProvider</c>) rather than a second clock implementation.
/// </summary>
public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly TodayIn(ZoneId zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.DateAt(UtcNow);
    }
}
