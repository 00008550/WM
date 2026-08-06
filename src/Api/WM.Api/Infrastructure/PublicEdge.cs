using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using WM.SharedKernel.Security;
using IPNetwork = System.Net.IPNetwork;

namespace WM.Api.Infrastructure;

/// <summary>
/// The two things a host reachable from the public internet needs and this one did not have:
/// knowing which visitor a request came from, and a bound on what an unauthenticated visitor may
/// spend.
///
/// <para>
/// They are one file because they are one mechanism. Nothing in <c>src/**</c> called
/// <c>UseForwardedHeaders</c>, so every request appeared to originate from the nginx container —
/// which means an address-keyed rate limiter added on its own would have put the entire internet
/// in a single bucket. It would have looked like a limiter and behaved like a global tap.
/// </para>
///
/// <para>
/// The trust list is the security-sensitive half. <c>ForwardedHeadersMiddleware</c> walks
/// <c>X-Forwarded-For</c> from the right, stopping at the first hop it does not trust, so a list
/// that is too broad lets a caller name its own client address — evading the very limiter added
/// here, and poisoning the logs. Too narrow and the limiter buckets every visitor together again.
/// Both failures are silent from outside, which is why <c>ForwardedHeadersTests</c> asserts each
/// of them rather than only the happy path.
/// </para>
/// </summary>
public static class PublicEdge
{
    /// <summary>Where the proxy-trust settings are read from.</summary>
    public const string ForwardedHeadersSection = "ForwardedHeaders";

    /// <summary>Where the anonymous rate limit is read from.</summary>
    public const string AnonymousRateLimitSection = "RateLimiting:Anonymous";

    /// <summary>
    /// Bucket key for a caller whose address is unknown — a transport Kestrel reports no remote
    /// address for. They share one bucket rather than escaping the limit: on a public host,
    /// "cannot identify the caller" must cost the caller, not the host.
    /// </summary>
    private const string UnknownClientKey = "unknown";

    public static IServiceCollection AddWmPublicEdge(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Read and validated here rather than inside Configure<T>: a deployment that mistypes a
        // CIDR range should fail while the host composes, with a message, instead of on the first
        // request through a middleware nobody is watching. Same reasoning as the signing-key
        // guard in IdentityModule.RegisterServices.
        var settings = PublicEdgeSettings.Read(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // X-Forwarded-For only. Proto and Host change what the app believes about the URL it
            // is serving, which is a separate decision with its own spoofing surface, and nothing
            // in this host emits an absolute URL or redirects to https today.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = settings.ForwardLimit;

            // Cleared rather than added to. The defaults are the IPv6 loopback in both lists, and
            // this is a trust boundary: what it holds should be exactly what the deployment said,
            // with nothing inherited that nobody chose.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var network in settings.TrustedNetworks)
                options.KnownIPNetworks.Add(network);
        });

        services.AddRateLimiter(options =>
        {
            // The framework's default rejection is 503, which says "this host is broken" when the
            // truth is "you are going too fast". 429 is the answer a client can act on.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = static (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(WmRateLimits.PublicAnonymous, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AnonymousPermitLimit,
                        Window = settings.AnonymousWindow,
                        // Queueing would hold a flood open on the server instead of rejecting it,
                        // which is the opposite of what a limit is for here.
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// The edge middleware, in the order the reasons demand — called by <c>Program.cs</c> and by
    /// the test host, so a test exercises this composition rather than a copy of it.
    ///
    /// <list type="number">
    ///   <item>Forwarded headers first, so everything after it decides on the visitor's address
    ///   rather than the proxy's.</item>
    ///   <item>CORS before the limiter, so a browser preflight — which carries no credentials and
    ///   is answered by the CORS middleware — does not spend the visitor's sign-in budget.</item>
    ///   <item>The limiter before authentication, so a flood is refused before any token is
    ///   validated or any database is touched, and an over-limit caller is told 429 rather than
    ///   401.</item>
    /// </list>
    ///
    /// <para>
    /// Request logging stays <em>outside</em> this, in <c>Program.cs</c>: a 429 that the limiter
    /// short-circuits still has to appear in the log, which it only does if the logging middleware
    /// wraps the limiter rather than following it.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseWmPublicEdge(this IApplicationBuilder app, string corsPolicy)
    {
        app.UseForwardedHeaders();
        app.UseCors(corsPolicy);
        app.UseRateLimiter();
        return app;
    }

    private static string ClientKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            return UnknownClientKey;

        // ::ffff:203.0.113.9 and 203.0.113.9 are the same visitor, and a dual-stack listener will
        // report either. Two buckets for one caller would double whatever the limit says.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.ToString();
    }
}

/// <summary>
/// What the edge is configured with, resolved once. Public so a test can read the defaults it is
/// asserting against instead of restating them.
/// </summary>
public sealed record PublicEdgeSettings(
    int ForwardLimit,
    IReadOnlyList<IPNetwork> TrustedNetworks,
    int AnonymousPermitLimit,
    TimeSpan AnonymousWindow)
{
    /// <summary>
    /// Two, because the demo topology is Caddy → nginx → Kestrel: by the time the API sees it the
    /// header reads <c>visitor, caddy</c> and nginx is the peer. Unwinding exactly two entries
    /// lands on the visitor; a third entry can only have been supplied by the visitor, and is
    /// ignored. Raising this without adding a proxy hop is how a caller gets to choose its own
    /// address.
    /// </summary>
    public const int DefaultForwardLimit = 2;

    /// <summary>
    /// Thirty sign-ins a minute from one address. Sized for the caller that is easy to forget: an
    /// office behind one NAT address, where every employee's morning sign-in and every SPA token
    /// refresh shares a bucket. Tight enough that credential stuffing is not free, loose enough
    /// that a customer's whole site is not locked out at 09:00.
    /// </summary>
    public const int DefaultAnonymousPermitLimit = 30;

    public static readonly TimeSpan DefaultAnonymousWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Loopback, and the address pool Docker allocates its bridge networks from
    /// (<c>172.16.0.0/12</c> — the compose stack declares no network, so it gets a generated
    /// <c>wm_default</c> bridge whose exact subnet depends on what else exists on the host).
    ///
    /// <para>
    /// It is a default, not a policy: a deployment that knows its own subnet should say so
    /// (<c>docker network inspect wm_default</c>) via <c>ForwardedHeaders__KnownNetworks__0</c>,
    /// which replaces this list wholesale rather than adding to it. Nothing routable from the
    /// internet is in here, and nothing public should ever be: the entry that matters is the
    /// address the reverse proxy connects <em>from</em>.
    /// </para>
    /// </summary>
    public static readonly string[] DefaultTrustedNetworks =
    [
        "127.0.0.0/8",
        "::1/128",
        "172.16.0.0/12",
    ];

    public static PublicEdgeSettings Read(IConfiguration configuration)
    {
        var forwarded = configuration.GetSection(PublicEdge.ForwardedHeadersSection);
        var configured = forwarded.GetSection("KnownNetworks").Get<string[]>();

        var forwardLimit = forwarded.GetValue("ForwardLimit", DefaultForwardLimit);
        if (forwardLimit < 1)
            throw new InvalidOperationException(
                $"{PublicEdge.ForwardedHeadersSection}:ForwardLimit is {forwardLimit}, so no "
                + "forwarded entry would ever be read and every visitor would share the reverse "
                + "proxy's address. Set it (env: ForwardedHeaders__ForwardLimit) to the number of "
                + "proxies in front of this host.");

        var rateLimit = configuration.GetSection(PublicEdge.AnonymousRateLimitSection);
        var permitLimit = rateLimit.GetValue("PermitLimit", DefaultAnonymousPermitLimit);
        if (permitLimit < 1)
            throw new InvalidOperationException(
                $"{PublicEdge.AnonymousRateLimitSection}:PermitLimit is {permitLimit}, which "
                + "refuses every anonymous request including sign-in. Set it (env: "
                + "RateLimiting__Anonymous__PermitLimit) to at least 1.");

        var window = rateLimit.GetValue("Window", DefaultAnonymousWindow);
        if (window <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{PublicEdge.AnonymousRateLimitSection}:Window must be longer than zero. Set it "
                + "(env: RateLimiting__Anonymous__Window) to a duration such as 00:01:00.");

        return new PublicEdgeSettings(
            forwardLimit,
            ParseTrustedNetworks(configured is { Length: > 0 } ? configured : DefaultTrustedNetworks),
            permitLimit,
            window);
    }

    private static IReadOnlyList<IPNetwork> ParseTrustedNetworks(IEnumerable<string?> values)
    {
        List<IPNetwork> networks = [];

        foreach (var value in values)
        {
            // Blank entries are dropped rather than refused: configuration arrays are set by
            // index, and an operator clearing an inherited entry writes an empty string.
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!IPNetwork.TryParse(value.Trim(), out var network))
                throw new InvalidOperationException(
                    $"{PublicEdge.ForwardedHeadersSection}:KnownNetworks contains \"{value}\", "
                    + "which is not an address range in CIDR form — a prefix length is required. "
                    + "Use e.g. 172.16.0.0/12 (env: ForwardedHeaders__KnownNetworks__0). The value "
                    + "is echoed because a network range is not a secret, and a typo in a "
                    + "crash-looping container is otherwise unfindable.");

            networks.Add(network);
        }

        // The one configuration that must never be accepted. ForwardedHeadersMiddleware skips its
        // trust check entirely when both known lists are empty, so an empty list does not mean
        // "trust nobody" — it means "trust everybody", and every visitor could then name the
        // address the limiter and the logs attribute them to.
        if (networks.Count == 0)
            throw new InvalidOperationException(
                $"{PublicEdge.ForwardedHeadersSection}:KnownNetworks is empty. An empty trust list "
                + "makes this host accept X-Forwarded-For from any caller, which would let a "
                + "visitor choose the client address the rate limiter buckets them under. Name the "
                + "network the reverse proxy connects from (env: "
                + "ForwardedHeaders__KnownNetworks__0=172.16.0.0/12).");

        return networks;
    }
}
