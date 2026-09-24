using WM.Modules.People.Contracts;
using WM.Modules.People.Services;
using WM.SharedKernel.Time;
using Xunit;
using Node = WM.Modules.People.Services.SiteZoneResolver.SiteNode;

namespace WM.Modules.People.Tests.Services;

/// <summary>
/// 008 P2: site → nearest ancestor with a zone → installation default, and which of the three
/// answered. The chain as a pure function; the database-backed half is exercised through
/// <c>SiteTimeZoneEndpointTests</c>.
/// </summary>
public sealed class SiteZoneResolverTests
{
    private static readonly InstallationZone Installation = new(ZoneId.Parse("Europe/Ljubljana"));

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Middle = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Leaf = Guid.Parse("10000000-0000-0000-0000-000000000003");

    private static Dictionary<Guid, Node> Tree(string? root, string? middle, string? leaf) => new()
    {
        [Root] = new Node(Root, null, root),
        [Middle] = new Node(Middle, Root, middle),
        [Leaf] = new Node(Leaf, Middle, leaf),
    };

    [Fact]
    public void A_site_with_its_own_zone_answers_for_itself()
    {
        var resolved = SiteZoneResolver.Resolve(Leaf, Tree("Asia/Tashkent", null, "America/Los_Angeles"), Installation);

        Assert.Equal("America/Los_Angeles", resolved.Zone.Id);
        Assert.Equal(ZoneSource.Site, resolved.Source);
        Assert.Equal(Leaf, resolved.FromSiteId);
    }

    [Fact]
    public void An_unset_child_inherits_the_nearest_ancestor_that_has_one()
    {
        var resolved = SiteZoneResolver.Resolve(Leaf, Tree("Asia/Tashkent", null, null), Installation);

        Assert.Equal("Asia/Tashkent", resolved.Zone.Id);
        Assert.Equal(ZoneSource.AncestorSite, resolved.Source);
        Assert.Equal(Root, resolved.FromSiteId);
    }

    [Fact]
    public void A_nearer_ancestor_wins_over_a_farther_one()
    {
        var resolved = SiteZoneResolver.Resolve(Leaf, Tree("Asia/Tashkent", "Pacific/Auckland", null), Installation);

        Assert.Equal("Pacific/Auckland", resolved.Zone.Id);
        Assert.Equal(Middle, resolved.FromSiteId);
    }

    [Fact]
    public void A_wholly_unset_chain_falls_back_to_the_installation_default_and_says_so()
    {
        var resolved = SiteZoneResolver.Resolve(Leaf, Tree(null, null, null), Installation);

        Assert.Equal(Installation.Zone, resolved.Zone);
        Assert.Equal(ZoneSource.Installation, resolved.Source);
        Assert.Null(resolved.FromSiteId);
    }

    [Fact]
    public void A_cyclic_parent_chain_terminates_at_the_installation_default()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var tree = new Dictionary<Guid, Node>
        {
            [a] = new Node(a, b, null),
            [b] = new Node(b, a, null),
        };

        var resolved = SiteZoneResolver.Resolve(a, tree, Installation);

        Assert.Equal(ZoneSource.Installation, resolved.Source);
    }

    [Fact]
    public void A_stored_value_that_is_not_a_zone_is_treated_as_unset_rather_than_fatal()
    {
        var resolved = SiteZoneResolver.Resolve(Leaf, Tree("Asia/Tashkent", null, "Europe/Nowhere"), Installation);

        Assert.Equal("Asia/Tashkent", resolved.Zone.Id);
        Assert.Equal(ZoneSource.AncestorSite, resolved.Source);
    }

    [Fact]
    public void An_unknown_site_resolves_to_the_installation_default()
    {
        var resolved = SiteZoneResolver.Resolve(Guid.NewGuid(), Tree("Asia/Tashkent", null, null), Installation);

        Assert.Equal(ZoneSource.Installation, resolved.Source);
    }
}
