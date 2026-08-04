using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WM.Api.Realtime;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Api.Tests.Realtime;

/// <summary>
/// Plan 003 decision 2: when a user's groups change, the open socket is re-grouped in place and
/// told, rather than serving the old scope until the next reconnect.
/// </summary>
public sealed class ScopeRevocationTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Manager = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private const string Connection = "conn-manager";

    private sealed class Harness
    {
        public FakeHub Hub { get; } = new();
        public AttendanceConnectionRegistry Registry { get; } = new();
        public Dictionary<Guid, EffectiveDataScope> Scopes { get; } = [];
        public AttendanceAudience Audience { get; }

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddScoped<IDataScopeResolver>(_ => new Resolver(Scopes));
            var provider = services.BuildServiceProvider();
            Audience = new AttendanceAudience(
                Hub, Registry, provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AttendanceAudience>.Instance);
        }

        private sealed class Resolver(Dictionary<Guid, EffectiveDataScope> scopes) : IDataScopeResolver
        {
            public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) =>
                throw new InvalidOperationException("Not reachable from a socket.");

            public Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default) =>
                Task.FromResult(scopes.TryGetValue(userId, out var scope) ? scope : EffectiveDataScope.None);
        }
    }

    [Fact]
    public async Task Narrowing_a_users_scope_moves_their_open_socket_and_tells_them()
    {
        var harness = new Harness();
        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteA);
        await harness.Audience.SubscribeAsync(Connection, Manager);
        Assert.Equal(new[] { AttendanceScopeGroups.Site(SiteA) }, harness.Hub.GroupManager.GroupsOf(Connection));

        // An administrator moves them from site A to site B.
        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteB);
        await harness.Audience.UserScopeChangedAsync([Manager]);

        Assert.Equal(new[] { AttendanceScopeGroups.Site(SiteB) }, harness.Hub.GroupManager.GroupsOf(Connection));
        Assert.DoesNotContain(Connection, harness.Hub.GroupManager.Members(AttendanceScopeGroups.Site(SiteA)));
        Assert.True(harness.Hub.Received(Connection, AttendanceAudience.ScopeChangedMethod));
    }

    [Fact]
    public async Task Revoking_a_users_scope_entirely_leaves_the_socket_in_no_group()
    {
        var harness = new Harness();
        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteA);
        await harness.Audience.SubscribeAsync(Connection, Manager);

        harness.Scopes[Manager] = EffectiveDataScope.None;
        await harness.Audience.UserScopeChangedAsync([Manager]);

        Assert.Empty(harness.Hub.GroupManager.GroupsOf(Connection));
        Assert.Empty(harness.Registry.GroupsFor(Connection));
    }

    [Fact]
    public async Task Every_socket_a_user_holds_is_re_grouped_not_just_the_first()
    {
        var harness = new Harness();
        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteA);
        await harness.Audience.SubscribeAsync("conn-desktop", Manager);
        await harness.Audience.SubscribeAsync("conn-phone", Manager);

        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteB);
        await harness.Audience.UserScopeChangedAsync([Manager]);

        foreach (var connection in new[] { "conn-desktop", "conn-phone" })
        {
            Assert.Equal(new[] { AttendanceScopeGroups.Site(SiteB) }, harness.Hub.GroupManager.GroupsOf(connection));
            Assert.True(harness.Hub.Received(connection, AttendanceAudience.ScopeChangedMethod));
        }
    }

    [Fact]
    public async Task A_scope_change_for_a_user_with_no_open_socket_does_nothing()
    {
        var harness = new Harness();
        await harness.Audience.UserScopeChangedAsync([Manager]);

        Assert.Empty(harness.Hub.HubClients.Deliveries);
    }

    [Fact]
    public async Task Re_subscribing_the_same_connection_issues_no_redundant_group_moves()
    {
        var harness = new Harness();
        harness.Scopes[Manager] = AttendanceScopeGroupTests.Sites(SiteA, SiteB);
        await harness.Audience.SubscribeAsync(Connection, Manager);
        await harness.Audience.SubscribeAsync(Connection, Manager);

        Assert.Equal(2, harness.Hub.GroupManager.GroupsOf(Connection).Count);
    }

    [Fact]
    public void The_hub_requires_the_attendance_permission_not_merely_authentication()
    {
        // A bare [Authorize] here is the defect: it authenticates and authorizes nothing, so a
        // self-service-only account could hold a socket. Program.cs asserts the same policy on
        // the route; this pins the attribute so removing either one is visible.
        var authorize = Attribute.GetCustomAttributes(typeof(AttendanceHub), typeof(AuthorizeAttribute))
            .Cast<AuthorizeAttribute>()
            .ToArray();

        Assert.Single(authorize);
        Assert.Equal(WmPermissions.AttendanceView, authorize[0].Policy);
    }
}
