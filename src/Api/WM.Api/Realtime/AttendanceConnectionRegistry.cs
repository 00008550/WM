using System.Collections.Concurrent;

namespace WM.Api.Realtime;

/// <summary>What a connection has to leave and join to reach a new set of groups.</summary>
public sealed record GroupDelta(IReadOnlyList<string> Leave, IReadOnlyList<string> Join)
{
    public static readonly GroupDelta Empty = new([], []);
}

/// <summary>
/// Which SignalR connections belong to which user, and which scope groups each one currently
/// sits in.
///
/// SignalR tracks group membership but will not tell you what a connection joined, and moving a
/// live socket needs its connection id. Both are needed to re-group an open connection when its
/// user's scope changes (plan 003 decision 2) rather than waiting for a reconnect.
///
/// In-process on purpose: it mirrors the default single-node hub lifetime manager. A Redis
/// backplane would have to move this with it — the punch fan-out would keep working, but a scope
/// change would only re-group the sockets attached to the node that handled the edit.
/// </summary>
public sealed class AttendanceConnectionRegistry
{
    private static readonly IReadOnlySet<string> NoGroups = new HashSet<string>(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Registration> _connections = new(StringComparer.Ordinal);

    private sealed record Registration(Guid? UserId, IReadOnlySet<string> Groups);

    /// <summary>
    /// Records the groups this connection should now be in and returns the difference from what
    /// it was in before, so the caller issues only the joins and leaves that actually changed.
    /// </summary>
    public GroupDelta Track(string connectionId, Guid? userId, IReadOnlyCollection<string> groups)
    {
        var next = new HashSet<string>(groups, StringComparer.Ordinal);
        var previous = _connections.TryGetValue(connectionId, out var existing) ? existing.Groups : NoGroups;
        _connections[connectionId] = new Registration(userId, next);

        return new GroupDelta(
            [.. previous.Where(group => !next.Contains(group))],
            [.. next.Where(group => !previous.Contains(group))]);
    }

    /// <summary>Forgets a connection and reports the groups it was in.</summary>
    public GroupDelta Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out var existing)
            ? new GroupDelta([.. existing.Groups], [])
            : GroupDelta.Empty;

    /// <summary>
    /// Every live connection belonging to a user.
    ///
    /// A scan, not an index. This runs only when an administrator edits a group — never on the
    /// punch path, which addresses groups and never looks at connections at all. A second
    /// dictionary keyed by user would buy nothing here and would have to be kept consistent with
    /// this one across concurrent connects and disconnects.
    /// </summary>
    public IReadOnlyList<string> ConnectionsFor(Guid userId) =>
        [.. _connections.Where(entry => entry.Value.UserId == userId).Select(entry => entry.Key)];

    /// <summary>The groups a connection is currently in. Diagnostics and tests.</summary>
    public IReadOnlySet<string> GroupsFor(string connectionId) =>
        _connections.TryGetValue(connectionId, out var existing) ? existing.Groups : NoGroups;

    public int ConnectionCount => _connections.Count;
}
