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
    /// Places a connection in the groups its user's resolved scope earns it. Called on connect
    /// and on every re-authorization; the registry makes it idempotent.
    /// </summary>
    public async Task SubscribeAsync(string connectionId, Guid? userId, CancellationToken ct = default)
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

    /// <summary>Forgets a closed connection.</summary>
    public async Task UnsubscribeAsync(string connectionId, CancellationToken ct = default)
    {
        var delta = registry.Remove(connectionId);
        // SignalR drops a closed connection from its own groups; clearing the registry is the
        // part that matters. Leaving explicitly is harmless and keeps the two in step should
        // this ever be called for a connection that is still open.
        foreach (var group in delta.Leave)
            await hub.Groups.RemoveFromGroupAsync(connectionId, group, ct);
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
