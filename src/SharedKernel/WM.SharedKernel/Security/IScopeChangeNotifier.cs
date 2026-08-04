namespace WM.SharedKernel.Security;

/// <summary>
/// Raised when the set of employees a user may see has changed — their group membership was
/// edited, or a group they belong to was re-scoped or deleted.
///
/// Query paths do not need this. They resolve scope per request, so the next request is already
/// correct. Live transports do: a SignalR connection's audience is decided once, when it
/// connects, so without a nudge a manager whose scope was narrowed keeps receiving the old
/// scope's punches until they happen to reconnect. Plan 003 decision 2 — revocation bites
/// within the session, not at next login.
/// </summary>
public interface IScopeChangeNotifier
{
    Task UserScopeChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

/// <summary>
/// The default for a host with no live transport (the worker, tests). A host that <i>does</i>
/// push live data replaces this registration — see <c>Program.cs</c>.
/// </summary>
public sealed class NullScopeChangeNotifier : IScopeChangeNotifier
{
    public Task UserScopeChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        Task.CompletedTask;
}
