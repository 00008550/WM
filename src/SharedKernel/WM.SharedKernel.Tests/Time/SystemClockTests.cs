using WM.SharedKernel.Time;
using Xunit;

namespace WM.SharedKernel.Tests.Time;

public sealed class SystemClockTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Reads_now_from_the_time_provider()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(instant, new SystemClock(new FixedTime(instant)).UtcNow);
    }

    [Fact]
    public void Two_zones_can_disagree_about_today_at_the_same_instant()
    {
        // 008's edge case: Auckland and Honolulu, one request, a calendar day apart — both right.
        var clock = new SystemClock(new FixedTime(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 9, 25), clock.TodayIn(ZoneId.Parse("Pacific/Auckland")));
        Assert.Equal(new DateOnly(2026, 9, 24), clock.TodayIn(ZoneId.Parse("Pacific/Honolulu")));
    }
}
