using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using WM.SharedKernel.Security;

namespace WM.Api.Realtime;

/// <summary>
/// Owns the answer to "which punches is this socket allowed to hear" — it resolves a connected
/// user's data scope and keeps the connection in exactly the groups that scope earns.
///
/// Also the <see cref="IScopeChangeNotifier"/> for this host: when a module reports that a
/// user's scope changed, every open connection of theirs is re-grouped in place and told, so a
/// narrowed manager stops receiving the old scope's punches immediately instead of at their next
/// login (plan 003 decision 2).
///
/// This revokes an <i>audience</i>, not a <i>session</i>. A user whose access was withdrawn still
/// holds a valid access token and can still call the HTTP API with it until it expires — see
/// "Deliberately not solved here" in <c>docs/plans/003-enforcement-gaps.md</c> before assuming
/// this file is all of revocation.
/// </summary>
public sealed class AttendanceAudience(
    IHubContext<AttendanceHub> hub,
    AttendanceConnectionRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<AttendanceAudience> logger) : IScopeChangeNotifier
{
    /// <summary>
    /// Client method telling a connection its audience just moved, so it can drop rows it is no
    /// longer entitled to instead of leaving them on screen. This is decision 2's "re-authorize"
    /// message; the groups have already been changed server-side by the time it arrives.
    /// </summary>
    public const string ScopeChangedMethod = "scopeChanged";

    /// <summary>
    /// One gate per connection.
    ///
    /// Resolving the scope, diffing it against what the connection currently holds, and issuing
    /// the resulting leaves and joins are one decision and have to happen as one step. Two
    /// overlapping calls for the same socket — a connect racing a scope edit, or two edits in
    /// quick succession — would otherwise interleave: each diffs against the other's registry
    /// write and then applies its own half, leaving the socket in a group set matching neither
    /// resolution. The failure mode is a connection sitting in a group it should have left, which
    /// is the leak this portion exists to close, and nothing errors when it happens.
    ///
    /// Per connection rather than one lock over the hub: the resolve inside the critical section
    /// is a database round trip, and one socket's slow resolve must not stall every other
    /// socket's re-grouping. Connections share no group state, so there is nothing to serialize
    /// between them.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Places a connection in the groups its user's resolved scope earns it. Called on connect
    /// and on every re-authorization; the registry makes it idempotent.
    /// </summary>
    public async Task SubscribeAsync(string connectionId, Guid? userId, CancellationToken ct = default)
    {
        var gate = GateFor(connectionId);
        // Outside the try on purpose: a cancelled wait never took the gate, so it must not
        // release one it does not hold.
        await gate.WaitAsync(ct);
        try
        {
            var scope = await ResolveAsync(userId, ct);
            var groups = AttendanceScopeGroups.ForScope(scope);
            var delta = registry.Track(connectionId, userId, groups);

            foreach (var group in delta.Leave)
                await hub.Groups.RemoveFromGroupAsync(connectionId, group, ct);
            foreach (var group in delta.Join)
                await hub.Groups.AddToGroupAsync(connectionId, group, ct);

            if (groups.Count == 0)
                logger.LogInformation(
                    "Realtime connection {ConnectionId} (user {UserId}) resolves to no scope group and will receive no punches",
                    connectionId, userId);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets a closed connection.</summary>
    public async Task UnsubscribeAsync(string connectionId, CancellationToken ct = default)
    {
        // Under the same gate as SubscribeAsync: a disconnect racing a re-group would otherwise
        // clear the registry while the other call is midway through applying groups.
        var gate = GateFor(connectionId);
        await gate.WaitAsync(ct);
        try
        {
            var delta = registry.Remove(connectionId);
            // SignalR drops a closed connection from its own groups; clearing the registry is the
            // part that matters. Leaving explicitly is harmless and keeps the two in step should
            // this ever be called for a connection that is still open.
            foreach (var group in delta.Leave)
                await hub.Groups.RemoveFromGroupAsync(connectionId, group, ct);
        }
        finally
        {
            gate.Release();
            // The connection is gone, so its gate goes with it — otherwise the dictionary grows
            // for the lifetime of the process. A caller already queued on this gate still holds
            // its own reference and completes normally; it just finds a registry that no longer
            // knows the connection.
            _gates.TryRemove(connectionId, out _);
        }
    }

    public async Task UserScopeChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        foreach (var userId in userIds.Distinct())
        {
            foreach (var connectionId in registry.ConnectionsFor(userId))
            {
                await SubscribeAsync(connectionId, userId, ct);
                await hub.Clients.Client(connectionId).SendAsync(ScopeChangedMethod, ct);
            }
        }
    }

    private SemaphoreSlim GateFor(string connectionId) =>
        _gates.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));

    private async Task<EffectiveDataScope> ResolveAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is not { } id)
            return EffectiveDataScope.None;

        try
        {
            // A fresh DI scope. This runs from a hub callback and from an administrator's
            // request thread, and neither owns a resolver this singleton may borrow.
            using var serviceScope = scopeFactory.CreateScope();
            var resolver = serviceScope.ServiceProvider.GetRequiredService<IDataScopeResolver>();
            return await resolver.GetScopeForUserAsync(id, ct);
        }
        catch (Exception ex)
        {
            // Failing to resolve must mean "hears nothing", never "keeps what it had". The
            // caller records the empty result, so the connection is emptied out rather than
            // left holding stale groups.
            logger.LogError(ex, "Could not resolve realtime scope for user {UserId}; granting no groups", id);
            return EffectiveDataScope.None;
        }
    }
}
