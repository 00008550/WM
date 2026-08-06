namespace WM.SharedKernel.Security;

/// <summary>
/// Names of the rate-limiting policies an endpoint may require, in the same spirit as
/// <see cref="WmPermissions"/>: the name is a contract between whoever maps the endpoint and
/// whoever composes the host, so it lives where both can see it.
///
/// <para>
/// The policies themselves are registered by the host — <c>WM.Api.Infrastructure.PublicEdge</c> —
/// because a limit is a property of the edge a deployment exposes, not of a module. A host that
/// maps an endpoint requiring one of these and never calls <c>AddWmPublicEdge</c> fails loudly on
/// the first request rather than silently serving it unlimited.
/// </para>
/// </summary>
public static class WmRateLimits
{
    /// <summary>
    /// Everything an unauthenticated caller can reach, keyed on the client address: the three
    /// sign-in transports and the readiness probe. One bucket per visitor, not one per endpoint —
    /// the thing being bounded is what one address may spend before it has proved anything.
    ///
    /// <para>
    /// Deliberately <em>not</em> applied to liveness (an orchestrator restarts containers on it,
    /// so throttling it would turn a flood into a rolling restart), to the realtime hub, or to any
    /// authenticated endpoint.
    /// </para>
    /// </summary>
    public const string PublicAnonymous = "wm-public-anonymous";

    public static readonly IReadOnlyList<string> All = [PublicAnonymous];
}
