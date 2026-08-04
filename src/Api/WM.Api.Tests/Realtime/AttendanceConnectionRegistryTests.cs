using WM.Api.Realtime;
using Xunit;

namespace WM.Api.Tests.Realtime;

public sealed class AttendanceConnectionRegistryTests
{
    private static readonly Guid UserA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Fact]
    public void A_new_connection_joins_everything_and_leaves_nothing()
    {
        var registry = new AttendanceConnectionRegistry();

        var delta = registry.Track("c1", UserA, ["g1", "g2"]);

        Assert.Empty(delta.Leave);
        Assert.Equal(new[] { "g1", "g2" }, delta.Join.OrderBy(g => g, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Re_tracking_reports_only_what_actually_changed()
    {
        var registry = new AttendanceConnectionRegistry();
        registry.Track("c1", UserA, ["g1", "g2"]);

        var delta = registry.Track("c1", UserA, ["g2", "g3"]);

        Assert.Equal(new[] { "g1" }, delta.Leave);
        Assert.Equal(new[] { "g3" }, delta.Join);
        Assert.Equal(new[] { "g2", "g3" }, registry.GroupsFor("c1").OrderBy(g => g, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Tracking_the_same_groups_twice_is_a_no_op()
    {
        var registry = new AttendanceConnectionRegistry();
        registry.Track("c1", UserA, ["g1"]);

        var delta = registry.Track("c1", UserA, ["g1"]);

        Assert.Empty(delta.Leave);
        Assert.Empty(delta.Join);
    }

    [Fact]
    public void Removing_a_connection_reports_the_groups_it_held_and_forgets_it()
    {
        var registry = new AttendanceConnectionRegistry();
        registry.Track("c1", UserA, ["g1", "g2"]);

        var delta = registry.Remove("c1");

        Assert.Equal(new[] { "g1", "g2" }, delta.Leave.OrderBy(g => g, StringComparer.Ordinal).ToArray());
        Assert.Empty(registry.GroupsFor("c1"));
        Assert.Equal(0, registry.ConnectionCount);
    }

    [Fact]
    public void Removing_an_unknown_connection_is_harmless()
    {
        var registry = new AttendanceConnectionRegistry();

        Assert.Empty(registry.Remove("never-seen").Leave);
    }

    [Fact]
    public void Connections_are_found_per_user_and_not_across_users()
    {
        var registry = new AttendanceConnectionRegistry();
        registry.Track("c1", UserA, ["g1"]);
        registry.Track("c2", UserA, ["g1"]);
        registry.Track("c3", UserB, ["g2"]);
        registry.Track("c4", userId: null, ["g3"]);

        Assert.Equal(new[] { "c1", "c2" }, registry.ConnectionsFor(UserA).OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "c3" }, registry.ConnectionsFor(UserB));
    }

    [Fact]
    public void The_registry_hands_out_a_snapshot_a_caller_cannot_widen()
    {
        // The stored set backs an authorization decision. If Track kept the caller's collection,
        // a later mutation of it would silently change what a live socket is entitled to.
        var registry = new AttendanceConnectionRegistry();
        var groups = new List<string> { "g1" };
        registry.Track("c1", UserA, groups);

        groups.Add("g2");

        Assert.Equal(new[] { "g1" }, registry.GroupsFor("c1"));
    }
}
