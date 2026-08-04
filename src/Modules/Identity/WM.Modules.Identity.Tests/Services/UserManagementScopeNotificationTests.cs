using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.Identity.Tests.Services;

/// <summary>
/// Editing a user can move their data scope, and a live connection has to hear about it.
///
/// <c>IsActive</c> and <c>EmployeeId</c> are not profile fields: <see cref="DataScopeResolver"/>
/// returns <c>None</c> for an inactive user, and the <c>Self</c> grant hangs off the employee
/// link. <see cref="SecurityGroupService"/> already notifies on every path that moves scope;
/// these pin the other half, which shipped without it.
///
/// The assertion is on *who was named*, not on a call count — the notifier's contract is a set of
/// affected users, and re-grouping the wrong user's socket is the failure that matters.
/// </summary>
public class UserManagementScopeNotificationTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private sealed class RecordingNotifier : IScopeChangeNotifier
    {
        public List<Guid[]> Notifications { get; } = [];

        public Task UserScopeChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
        {
            Notifications.Add([.. userIds]);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness : IDisposable
    {
        public IdentityDbContext Db { get; }
        public RecordingNotifier Notifier { get; } = new();
        public UserManagementService Users { get; }

        public Harness()
        {
            Db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"identity-{Guid.NewGuid():N}")
                .Options);
            Users = new UserManagementService(Db, new PasswordHasher<User>(), Notifier);
        }

        public User Seed(bool isActive = true, Guid? employeeId = null)
        {
            var user = new User
            {
                UserName = "kim.ops",
                Email = "kim@example.com",
                DisplayName = "Kim Ops",
                IsActive = isActive,
                EmployeeId = employeeId,
            };
            Db.Users.Add(user);
            Db.SaveChanges();
            return user;
        }

        public void Dispose() => Db.Dispose();
    }

    private static UpdateUserRequest Edit(
        User user, string? displayName = null, bool? isActive = null, Guid? employeeId = null) =>
        new(displayName ?? user.DisplayName, user.Email, isActive ?? user.IsActive,
            employeeId ?? user.EmployeeId, []);

    [Fact]
    public async Task Deactivating_a_user_tells_the_scope_notifier()
    {
        using var harness = new Harness();
        var user = harness.Seed(employeeId: Alice);

        var result = await harness.Users.UpdateAsync(
            user.Id, Edit(user, isActive: false), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(new[] { user.Id }, Assert.Single(harness.Notifier.Notifications));
    }

    [Fact]
    public async Task Re_activating_a_user_tells_the_scope_notifier_too()
    {
        // Widening is as much a scope change as narrowing: the socket they hold was placed in no
        // group when they were disabled, and nothing else will move it back.
        using var harness = new Harness();
        var user = harness.Seed(isActive: false, employeeId: Alice);

        await harness.Users.UpdateAsync(user.Id, Edit(user, isActive: true), CancellationToken.None);

        Assert.Equal(new[] { user.Id }, Assert.Single(harness.Notifier.Notifications));
    }

    [Fact]
    public async Task Re_linking_a_user_to_a_different_employee_tells_the_scope_notifier()
    {
        using var harness = new Harness();
        var user = harness.Seed(employeeId: Alice);

        await harness.Users.UpdateAsync(user.Id, Edit(user, employeeId: Bob), CancellationToken.None);

        Assert.Equal(new[] { user.Id }, Assert.Single(harness.Notifier.Notifications));
    }

    [Fact]
    public async Task Linking_an_unlinked_user_to_an_employee_tells_the_scope_notifier()
    {
        using var harness = new Harness();
        var user = harness.Seed();

        await harness.Users.UpdateAsync(user.Id, Edit(user, employeeId: Alice), CancellationToken.None);

        Assert.Equal(new[] { user.Id }, Assert.Single(harness.Notifier.Notifications));
    }

    [Fact]
    public async Task Unlinking_a_users_employee_tells_the_scope_notifier()
    {
        // Not expressible through Edit's defaults — clearing the link means passing null, which
        // is also "leave it alone". Built explicitly so the case is actually covered.
        using var harness = new Harness();
        var user = harness.Seed(employeeId: Alice);

        await harness.Users.UpdateAsync(
            user.Id,
            new UpdateUserRequest(user.DisplayName, user.Email, user.IsActive, null, []),
            CancellationToken.None);

        Assert.Equal(new[] { user.Id }, Assert.Single(harness.Notifier.Notifications));
        Assert.Null((await harness.Db.Users.FindAsync(user.Id))!.EmployeeId);
    }

    [Fact]
    public async Task An_edit_that_moves_no_scope_stays_quiet()
    {
        using var harness = new Harness();
        var user = harness.Seed(employeeId: Alice);

        var result = await harness.Users.UpdateAsync(
            user.Id, Edit(user, displayName: "Kim O."), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public async Task A_rejected_edit_tells_nobody()
    {
        // The email belongs to someone else, so the update is refused before anything is written.
        // Announcing a scope change that did not happen would re-group a socket to the scope it
        // already has — harmless here, but the notifier is the thing that revokes access and it
        // must only ever report what the database actually did.
        using var harness = new Harness();
        var user = harness.Seed(employeeId: Alice);
        harness.Db.Users.Add(new User
        {
            UserName = "someone.else",
            Email = "taken@example.com",
            DisplayName = "Someone Else",
        });
        await harness.Db.SaveChangesAsync();

        var result = await harness.Users.UpdateAsync(
            user.Id,
            new UpdateUserRequest(user.DisplayName, "taken@example.com", false, null, []),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(harness.Notifier.Notifications);
    }

    [Fact]
    public async Task An_edit_to_a_user_that_does_not_exist_tells_nobody()
    {
        using var harness = new Harness();

        var result = await harness.Users.UpdateAsync(
            Guid.NewGuid(),
            new UpdateUserRequest("Ghost", "ghost@example.com", true, null, []),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(harness.Notifier.Notifications);
    }
}
