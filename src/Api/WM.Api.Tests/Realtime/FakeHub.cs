using Microsoft.AspNetCore.SignalR;
using WM.Api.Realtime;
using Xunit;

namespace WM.Api.Tests.Realtime;

/// <summary>
/// An in-memory stand-in for SignalR's hub context: real group bookkeeping, real
/// group-addressed delivery, and a record of who received what.
///
/// It exists because the thing under test is *who receives a message*, and that question cannot
/// be answered by asserting on a mock's call arguments — the group names are an implementation
/// detail, the recipients are the contract. Delivery is recorded per connection and duplicates
/// are kept, so a connection receiving the same punch twice shows up as a failure rather than
/// being silently collapsed.
///
/// <see cref="FakeHubClients.All"/> counts the attempt and then throws. Reaching for it is the
/// defect this portion removes, so a regression should fail loudly — but the producer swallows
/// every exception on the realtime leg by design, so "the throw made the test pass with no
/// recipients" is a real trap. Hence the counter: a test asserting nobody received the punch
/// asserts <see cref="FakeHubClients.BroadcastAttempts"/> is zero as well, which is the
/// difference between "correctly addressed to nobody" and "broadcast, and it blew up".
/// </summary>
internal sealed class FakeHub : IHubContext<AttendanceHub>
{
    public FakeGroupManager GroupManager { get; } = new();
    public FakeHubClients HubClients { get; }

    public FakeHub() => HubClients = new FakeHubClients(GroupManager);

    IHubClients IHubContext<AttendanceHub>.Clients => HubClients;
    IGroupManager IHubContext<AttendanceHub>.Groups => GroupManager;

    /// <summary>Messages a connection received, oldest first.</summary>
    public IReadOnlyList<Delivery> DeliveriesTo(string connectionId) =>
        [.. HubClients.Deliveries.Where(d => d.ConnectionId == connectionId)];

    public bool Received(string connectionId, string method) =>
        HubClients.Deliveries.Any(d => d.ConnectionId == connectionId && d.Method == method);

    /// <summary>Asserts nothing was delivered <i>and</i> nothing was broadcast.</summary>
    public void AssertNothingReached()
    {
        Assert.Empty(HubClients.Deliveries);
        Assert.Equal(0, HubClients.BroadcastAttempts);
    }
}

internal sealed record Delivery(string ConnectionId, string Method, object?[] Args);

internal sealed class FakeGroupManager : IGroupManager
{
    private readonly Dictionary<string, HashSet<string>> _groups = new(StringComparer.Ordinal);

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (!_groups.TryGetValue(groupName, out var members))
            _groups[groupName] = members = new HashSet<string>(StringComparer.Ordinal);
        members.Add(connectionId);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (_groups.TryGetValue(groupName, out var members))
            members.Remove(connectionId);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<string> Members(string groupName) =>
        _groups.TryGetValue(groupName, out var members) ? [.. members] : [];

    /// <summary>Every group this connection is in — the server-side view of its audience.</summary>
    public IReadOnlyCollection<string> GroupsOf(string connectionId) =>
        [.. _groups.Where(g => g.Value.Contains(connectionId)).Select(g => g.Key)];

    internal IEnumerable<string> Expand(IReadOnlyList<string> groupNames) =>
        // Deliberately not de-duplicated across groups: this is what SignalR's default lifetime
        // manager does, so a connection in two addressed groups really would be sent two copies.
        groupNames.SelectMany(Members);
}

internal sealed class FakeHubClients(FakeGroupManager groups) : IHubClients
{
    public List<Delivery> Deliveries { get; } = [];

    /// <summary>How many times something asked for an unscoped broadcast. Must stay zero.</summary>
    public int BroadcastAttempts { get; private set; }

    public IClientProxy All
    {
        get
        {
            BroadcastAttempts++;
            throw new InvalidOperationException(
                "Clients.All is an unscoped broadcast. The punch feed must address scope groups (plan 003 P1).");
        }
    }

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;

    public IClientProxy Client(string connectionId) => new FakeClientProxy(this, [connectionId]);

    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy(this, connectionIds);

    public IClientProxy Group(string groupName) => Groups([groupName]);

    public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
        new FakeClientProxy(this, [.. groups.Expand(groupNames)]);

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        new FakeClientProxy(this, [.. groups.Members(groupName).Where(c => !excludedConnectionIds.Contains(c))]);

    public IClientProxy User(string userId) => throw new NotSupportedException("Not used by the attendance hub.");

    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException("Not used by the attendance hub.");

    private sealed class FakeClientProxy(FakeHubClients owner, IReadOnlyList<string> recipients) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            foreach (var connectionId in recipients)
                owner.Deliveries.Add(new Delivery(connectionId, method, args));
            return Task.CompletedTask;
        }
    }
}
