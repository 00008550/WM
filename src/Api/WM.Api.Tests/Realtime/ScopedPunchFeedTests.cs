using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WM.Api.Infrastructure;
using WM.Api.Realtime;
using WM.Modules.TimeAttendance.Contracts;
using WM.SharedKernel.Events;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Realtime;

/// <summary>
/// End to end across the realtime leg: connections are subscribed the way the hub subscribes
/// them, a punch is published the way <c>PunchService</c> publishes it, and the assertions are
/// about which connections received the message.
///
/// This is the test that would have caught PHASE-AUDIT.md A1.
/// </summary>
public sealed class ScopedPunchFeedTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SiteC = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid DeptA = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid Punched = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Unwatched = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid SelfServiceEmployee = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    // Users, one connection each.
    private const string Admin = "conn-admin";
    private const string InScopeManager = "conn-manager-site-a";
    private const string OutOfScopeManager = "conn-manager-site-b";
    private const string DepartmentManager = "conn-manager-dept-a";
    private const string SelfServiceUser = "conn-employee";
    private const string GrantedNothing = "conn-no-scope";

    private static readonly Dictionary<string, Guid> UserIds = new()
    {
        [Admin] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
        [InScopeManager] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
        [OutOfScopeManager] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"),
        [DepartmentManager] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004"),
        [SelfServiceUser] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005"),
        [GrantedNothing] = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006"),
    };

    private static readonly Dictionary<Guid, EffectiveDataScope> Scopes = new()
    {
        [UserIds[Admin]] = EffectiveDataScope.All(),
        [UserIds[InScopeManager]] = AttendanceScopeGroupTests.Sites(SiteA),
        [UserIds[OutOfScopeManager]] = AttendanceScopeGroupTests.Sites(SiteB),
        [UserIds[DepartmentManager]] = AttendanceScopeGroupTests.Departments(DeptA),
        // A self-service-only account: linked to its own employee record and nothing else.
        [UserIds[SelfServiceUser]] = AttendanceScopeGroupTests.Self(SelfServiceEmployee),
        [UserIds[GrantedNothing]] = EffectiveDataScope.None,
    };

    private sealed class Fixture
    {
        public FakeHub Hub { get; } = new();
        public AttendanceConnectionRegistry Registry { get; } = new();
        public Dictionary<Guid, EffectiveDataScope> Scopes { get; }
        public AttendanceAudience Audience { get; }
        public BroadcastingEventStreamProducer Producer { get; }

        public Fixture(Dictionary<Guid, EffectiveDataScope>? scopes = null)
        {
            Scopes = scopes ?? new Dictionary<Guid, EffectiveDataScope>(ScopedPunchFeedTests.Scopes);

            // A real container, so the audience resolves scope through a real IServiceScopeFactory
            // exactly as it does in the host.
            var services = new ServiceCollection();
            services.AddScoped<IDataScopeResolver>(_ => new StubScopeResolver(Scopes));
            var provider = services.BuildServiceProvider();

            Audience = new AttendanceAudience(
                Hub, Registry, provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AttendanceAudience>.Instance);

            // No Kafka broker configured, so the durable leg is a no-op and the test is purely
            // about the realtime leg.
            var kafka = new KafkaEventStreamProducer(
                new ConfigurationBuilder().Build(), NullLogger<KafkaEventStreamProducer>.Instance);
            Producer = new BroadcastingEventStreamProducer(
                kafka, Hub, NullLogger<BroadcastingEventStreamProducer>.Instance);
        }

        public Task ConnectAsync(string connectionId) =>
            Audience.SubscribeAsync(connectionId, UserIds[connectionId]);

        public async Task ConnectAllAsync()
        {
            foreach (var connectionId in UserIds.Keys)
                await ConnectAsync(connectionId);
        }

        public Task PunchAsync(Guid employeeId, Guid siteId, Guid? departmentId) =>
            Producer.PublishAsync(EventTopics.Punches, "E1001", new PunchRecorded(
                Guid.NewGuid(), employeeId, "E1001", "Test Employee", siteId, departmentId,
                DateTimeOffset.UtcNow, "In", "Web"));

        public IReadOnlyList<string> Recipients =>
            [.. Hub.HubClients.Deliveries.Where(d => d.Method == "punchRecorded").Select(d => d.ConnectionId)];
    }

    [Fact]
    public async Task Only_connections_whose_scope_contains_the_employee_receive_the_punch()
    {
        var fixture = new Fixture();
        await fixture.ConnectAllAsync();

        await fixture.PunchAsync(Punched, SiteA, DeptA);

        // The exact recipient set, not a containment check: "who else got it" is the whole
        // question, and a Contains-only assertion is what let the broadcast pass review.
        Assert.Equal(
            new[] { Admin, DepartmentManager, InScopeManager },
            fixture.Recipients.OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Nobody_receives_a_punch_for_an_employee_no_connected_user_can_see()
    {
        // Everyone connected is scoped away from site C, and no administrator is online.
        var scopes = new Dictionary<Guid, EffectiveDataScope>(Scopes);
        scopes[UserIds[Admin]] = AttendanceScopeGroupTests.Sites(SiteA);

        var fixture = new Fixture(scopes);
        await fixture.ConnectAllAsync();

        await fixture.PunchAsync(Unwatched, SiteC, departmentId: null);

        fixture.Hub.AssertNothingReached();
    }

    [Fact]
    public async Task A_self_service_user_receives_only_their_own_punch()
    {
        var fixture = new Fixture();
        await fixture.ConnectAsync(SelfServiceUser);

        await fixture.PunchAsync(Punched, SiteA, DeptA);
        fixture.Hub.AssertNothingReached();

        await fixture.PunchAsync(SelfServiceEmployee, SiteA, DeptA);
        Assert.Equal(new[] { SelfServiceUser }, fixture.Recipients);
    }

    [Fact]
    public async Task A_connection_that_earns_no_group_is_recorded_as_having_none()
    {
        // The default has to be "in no group", not "in a default group" — a connection the
        // resolver cannot place must hear nothing rather than everything.
        var fixture = new Fixture();
        await fixture.ConnectAsync(GrantedNothing);

        Assert.Empty(fixture.Registry.GroupsFor(GrantedNothing));
        Assert.Empty(fixture.Hub.GroupManager.GroupsOf(GrantedNothing));
    }

    [Fact]
    public async Task An_unauthenticated_connection_earns_no_group()
    {
        var fixture = new Fixture();
        await fixture.Audience.SubscribeAsync("conn-anonymous", userId: null);

        Assert.Empty(fixture.Registry.GroupsFor("conn-anonymous"));
        await fixture.PunchAsync(Punched, SiteA, DeptA);
        fixture.Hub.AssertNothingReached();
    }

    [Fact]
    public async Task A_disconnected_connection_stops_receiving()
    {
        var fixture = new Fixture();
        await fixture.ConnectAsync(InScopeManager);
        await fixture.Audience.UnsubscribeAsync(InScopeManager);

        await fixture.PunchAsync(Punched, SiteA, DeptA);

        fixture.Hub.AssertNothingReached();
        Assert.Equal(0, fixture.Registry.ConnectionCount);
    }

    [Fact]
    public async Task An_event_on_the_punch_topic_that_is_not_a_punch_reaches_nobody()
    {
        // Fail closed on an unrecognised payload: there is no audience rule for it, so it gets
        // no audience. FakeHubClients.All throws, so a fallback to broadcast fails here loudly.
        var fixture = new Fixture();
        await fixture.ConnectAllAsync();

        await fixture.Producer.PublishAsync(EventTopics.Punches, "key", new { Something = "else" });

        fixture.Hub.AssertNothingReached();
    }

    [Fact]
    public async Task Events_on_other_topics_are_not_pushed_to_the_hub_at_all()
    {
        var fixture = new Fixture();
        await fixture.ConnectAllAsync();

        await fixture.Producer.PublishAsync(EventTopics.Audit, "key", new PunchRecorded(
            Guid.NewGuid(), Punched, "E1001", "Test Employee", SiteA, DeptA,
            DateTimeOffset.UtcNow, "In", "Web"));

        fixture.Hub.AssertNothingReached();
    }

    private sealed class StubScopeResolver(Dictionary<Guid, EffectiveDataScope> scopes) : IDataScopeResolver
    {
        public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "The hub must resolve by explicit user id: there is no HttpContext on a socket.");

        public Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(scopes.TryGetValue(userId, out var scope) ? scope : EffectiveDataScope.None);
    }
}
