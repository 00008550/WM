using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using WM.Api.Infrastructure;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// 006 P3's first half: which address this host believes a request came from.
///
/// <para>
/// Nothing in <c>src/**</c> called <c>UseForwardedHeaders</c>, so every request appeared to come
/// from the nginx container. That is why these tests come before the rate-limit ones: an
/// address-keyed limiter on top of that fault is not a weak limiter, it is a single bucket holding
/// the entire internet.
/// </para>
///
/// <para>
/// The other half is a spoofing surface, so the assertions run in both directions — a trusted hop
/// is unwound, an untrusted one is ignored, and an entry beyond the configured number of proxies
/// is ignored even when the hop that carried it is trusted. Every address below is from a
/// documentation range (RFC 5737 / 3849-style), so none of them can ever be a real host.
/// </para>
/// </summary>
public sealed class ForwardedHeadersTests
{
    private const string ClientIpProbe = "/probe/client-ip";

    /// <summary>A visitor on the public internet.</summary>
    private const string Visitor = "198.51.100.7";

    /// <summary>The portal's nginx, on the compose bridge — the peer Kestrel actually sees.</summary>
    private const string Nginx = "172.20.0.5";

    /// <summary>Caddy, the second hop the demo adds in front of nginx.</summary>
    private const string Caddy = "172.20.0.9";

    /// <summary>Someone talking to this host directly, from nowhere we trust.</summary>
    private const string Stranger = "203.0.113.9";

    /// <summary>The address that caller would rather be attributed to.</summary>
    private const string Forged = "203.0.113.99";

    [Fact]
    public async Task A_request_with_no_forwarded_header_is_attributed_to_the_peer()
    {
        await using var host = await StartAsync();

        Assert.Equal(Stranger, await ClientAddressSeenBy(host, peer: Stranger, forwardedFor: null));
    }

    [Fact]
    public async Task A_request_through_the_reverse_proxy_is_attributed_to_the_visitor()
    {
        // The single-proxy shape: docker-compose.prod.yml publishes nginx, and nginx.conf:37 sets
        // X-Forwarded-For to $proxy_add_x_forwarded_for.
        await using var host = await StartAsync();

        Assert.Equal(Visitor, await ClientAddressSeenBy(host, peer: Nginx, forwardedFor: Visitor));
    }

    [Fact]
    public async Task Both_proxy_hops_of_the_demo_topology_are_unwound()
    {
        // Caddy → nginx → Kestrel. By the time the API sees it the header reads "visitor, caddy"
        // and the peer is nginx, so landing on the visitor takes ForwardLimit = 2.
        await using var host = await StartAsync();

        Assert.Equal(
            Visitor,
            await ClientAddressSeenBy(host, peer: Nginx, forwardedFor: $"{Visitor}, {Caddy}"));
    }

    [Fact]
    public async Task A_visitor_who_sends_its_own_forwarded_header_is_still_attributed_correctly()
    {
        // The everyday spoof attempt: the visitor sets X-Forwarded-For, Caddy appends the address
        // it actually saw and nginx appends Caddy. Two things stop it — the unwind reaches the
        // visitor's real (public, untrusted) address and halts there, and the limit is already
        // spent. Asserted with three entries because that is the shape a forged header takes.
        await using var host = await StartAsync();

        Assert.Equal(
            Visitor,
            await ClientAddressSeenBy(
                host, peer: Nginx, forwardedFor: $"{Forged}, {Visitor}, {Caddy}"));
    }

    [Fact]
    public async Task The_forward_limit_stops_the_unwind_when_every_forged_hop_looks_internal()
    {
        // The case where ForwardLimit is the only thing left doing the work: the caller forges a
        // hop that is itself inside the trusted range, so the chain-of-trust check would keep
        // walking. Two entries is what the topology has, so the walk stops on the forged internal
        // address and never reaches the public one the caller chose — which is the difference
        // between "some internal caller" and "this visitor is whoever they say they are".
        await using var host = await StartAsync();

        Assert.Equal(
            "172.16.9.9",
            await ClientAddressSeenBy(
                host, peer: Nginx, forwardedFor: $"{Forged}, 172.16.9.9, {Caddy}"));
    }

    [Fact]
    public async Task An_untrusted_caller_cannot_choose_its_own_address()
    {
        // Nothing between this caller and Kestrel, so the header is theirs and is ignored. Without
        // a trust list this is the request that would let one attacker spend everyone else's
        // budget — or evade the limiter entirely by picking a new address per request.
        await using var host = await StartAsync();

        Assert.Equal(Stranger, await ClientAddressSeenBy(host, peer: Stranger, forwardedFor: Visitor));
    }

    [Fact]
    public async Task A_deployment_can_replace_the_default_trust_list_with_its_own()
    {
        // The compose bridge's subnet is generated, so the default covers Docker's whole pool. A
        // deployment that knows its own subnet should narrow it, and narrowing has to actually
        // remove trust rather than add to it.
        await using var host = await StartAsync(("ForwardedHeaders:KnownNetworks:0", "10.10.0.0/16"));

        Assert.Equal(Nginx, await ClientAddressSeenBy(host, peer: Nginx, forwardedFor: Visitor));
        Assert.Equal(Visitor, await ClientAddressSeenBy(host, peer: "10.10.0.3", forwardedFor: Visitor));
    }

    [Fact]
    public void A_trust_list_that_names_nothing_is_refused()
    {
        // The failure mode that reads as safe and is not: ForwardedHeadersMiddleware skips its
        // trust check altogether when both known lists are empty, so "trust nobody" is spelled
        // exactly like "trust everybody".
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: Settings(("ForwardedHeaders:KnownNetworks:0", ""))));

        Assert.Contains("empty", fault.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForwardedHeaders__KnownNetworks__0", fault.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("172.16.0.0")]
    [InlineData("nginx")]
    [InlineData("172.16.0.0/33")]
    public void A_range_that_is_not_a_range_is_refused(string notARange)
    {
        // A missing prefix is the likely typo, and it must not be read as "one address" or as
        // anything else. The message echoes the value because a network range is not a secret and
        // a typo is otherwise unfindable in a crash-looping container.
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(
                configurationOverrides: Settings(("ForwardedHeaders:KnownNetworks:0", notARange))));

        Assert.Contains(notARange, fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_range_written_with_host_bits_set_still_means_its_network()
    {
        // Worth pinning rather than assuming, because it is a trust boundary: System.Net.IPNetwork
        // accepts 172.20.0.1/16 and matches on the prefix, so an operator who typed the gateway
        // address where a range belongs gets the /16 they meant — not a single host, and not a
        // wider range.
        await using var host = await StartAsync(("ForwardedHeaders:KnownNetworks:0", "172.20.0.1/16"));

        Assert.Equal(Visitor, await ClientAddressSeenBy(host, peer: Nginx, forwardedFor: Visitor));
        Assert.Equal(Stranger, await ClientAddressSeenBy(host, peer: Stranger, forwardedFor: Visitor));
    }

    [Fact]
    public void A_forward_limit_of_zero_is_refused()
    {
        // Zero would read no forwarded entry at all, which is the exact defect this portion is
        // closing — every visitor sharing the reverse proxy's address — expressed as a setting.
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: Settings(("ForwardedHeaders:ForwardLimit", "0"))));

        Assert.Contains("ForwardedHeaders__ForwardLimit", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_defaults_are_the_topology_this_repository_actually_ships()
    {
        // docker-compose.prod.yml declares no network, so the stack runs on a generated bridge out
        // of Docker's default pool; the demo adds Caddy in front of nginx, which is the second hop.
        var settings = PublicEdgeSettings.Read(new ConfigurationBuilder().Build());

        Assert.Equal(2, settings.ForwardLimit);
        Assert.Contains(settings.TrustedNetworks, network => network.Contains(IPAddress.Parse(Nginx)));
        Assert.DoesNotContain(settings.TrustedNetworks, network => network.Contains(IPAddress.Parse(Stranger)));
    }

    private static Task<ApiTestHost> StartAsync(params (string Key, string Value)[] configuration) =>
        ApiTestHost.StartAsync(
            app => app.MapGet(
                    ClientIpProbe,
                    (HttpContext context) => Results.Text(
                        context.Connection.RemoteIpAddress?.ToString() ?? "none"))
                .AllowAnonymous(),
            configurationOverrides: Settings(configuration));

    /// <summary>
    /// What <c>Connection.RemoteIpAddress</c> reads as by the time an endpoint runs — which is
    /// what the rate limiter partitions on and what a log line would carry.
    /// </summary>
    private static async Task<string> ClientAddressSeenBy(
        ApiTestHost host, string peer, string? forwardedFor)
    {
        using var client = host.ClientFrom(peer);

        var request = new HttpRequestMessage(HttpMethod.Get, ClientIpProbe);
        if (forwardedFor is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string?> Settings(params (string Key, string Value)[] configuration) =>
        configuration.ToDictionary(setting => setting.Key, setting => (string?)setting.Value);
}
