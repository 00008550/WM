using WM.SharedKernel.Time;
using Xunit;

namespace WM.SharedKernel.Tests.Time;

public sealed class ZoneIdTests
{
    [Theory]
    [InlineData("Europe/Ljubljana")]
    [InlineData("UTC")]
    [InlineData("Asia/Tashkent")]
    [InlineData("America/Los_Angeles")]
    public void Accepts_an_IANA_id_and_keeps_it(string id)
    {
        var zone = ZoneId.Parse(id);

        Assert.Equal(id, zone.Id);
        Assert.True(ZoneId.TryParse(id, out var again));
        Assert.Equal(zone, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Europe/Nowhere")]
    [InlineData("europe/ljubljana ")]
    [InlineData(" Europe/Ljubljana")]
    public void Refuses_what_is_not_a_zone(string? id)
    {
        Assert.False(ZoneId.TryParse(id, out _, out var fault));
        Assert.False(string.IsNullOrEmpty(fault));
        Assert.Throws<ArgumentException>(() => ZoneId.Parse(id));
    }

    [Theory]
    [InlineData("Central European Standard Time")]
    [InlineData("Pacific Standard Time")]
    [InlineData("West Asia Standard Time")]
    public void Refuses_a_Windows_id_on_every_OS(string windowsId)
    {
        // FindSystemTimeZoneById resolves these on both Windows and Linux (ICU converts), so this
        // is the assertion that keeps a developer's Windows box from accepting what the plan says
        // is refused on Linux.
        Assert.False(ZoneId.TryParse(windowsId, out _, out var fault));
        Assert.Contains("Windows", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fault_names_the_value_it_refused()
    {
        ZoneId.TryParse("Europe/Nowhere", out _, out var fault);

        Assert.Contains("Europe/Nowhere", fault, StringComparison.Ordinal);
    }

    [Theory]
    // 008's own boundary cases: the date is the zone's, not UTC's.
    [InlineData("Asia/Tashkent", "2026-03-01T23:30:00Z", "2026-03-02")]
    [InlineData("America/Los_Angeles", "2026-03-02T00:30:00Z", "2026-03-01")]
    [InlineData("Europe/Ljubljana", "2026-07-22T22:30:00Z", "2026-07-23")]
    [InlineData("UTC", "2026-07-22T22:30:00Z", "2026-07-22")]
    public void DateAt_reads_the_wall_clock_of_the_zone(string id, string instant, string expected)
    {
        var date = ZoneId.Parse(id).DateAt(DateTimeOffset.Parse(instant));

        Assert.Equal(DateOnly.Parse(expected), date);
    }

    [Fact]
    public void Equality_is_by_id()
    {
        Assert.Equal(ZoneId.Parse("UTC"), ZoneId.Utc);
        Assert.NotEqual(ZoneId.Parse("Europe/Ljubljana"), ZoneId.Utc);
        Assert.Equal(ZoneId.Utc.GetHashCode(), ZoneId.Parse("UTC").GetHashCode());
    }
}
