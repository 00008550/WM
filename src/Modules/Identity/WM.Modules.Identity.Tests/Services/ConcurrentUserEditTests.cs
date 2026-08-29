using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using WM.SharedKernel.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.Identity.Tests.Services;

/// <summary>
/// The Identity half of 011 P5. Same defect, same shape, one difference worth stating: the users
/// screen saves the profile and the group membership as two calls behind one button, so a stale
/// profile write has to be refused <b>before</b> the membership call is made — which it is, because
/// <c>users.component.ts</c> chains the second onto the first's success.
///
/// <para>
/// The mutation: drop <c>.IsConcurrencyToken()</c> from <c>IdentityDbContext</c>'s <c>User</c>
/// configuration and <see cref="The_second_of_two_edits_from_the_same_load_is_refused"/> succeeds
/// with B's display name on the record.
/// </para>
/// </summary>
public class ConcurrentUserEditTests
{
    [Fact]
    public async Task The_second_of_two_edits_from_the_same_load_is_refused()
    {
        using var harness = new Harness();
        var user = harness.Seed();
        var shared = user.Version;

        var first = await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim First", shared), default);
        Assert.True(first.Succeeded, first.Error);

        var second = await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim Second", shared), default);

        Assert.False(second.Succeeded);
        // A conflict, not bad input — the endpoint turns this flag into a 409 rather than a 400.
        Assert.True(second.Conflict);
        Assert.Equal(ConcurrentEdit.Message, second.Error);
        Assert.Equal("Kim First", harness.Stored(user.Id).DisplayName);
    }

    [Fact]
    public async Task Reloading_after_a_conflict_lets_the_second_edit_land()
    {
        // The token has to move forward with each write, or a conflicted record is bricked.
        using var harness = new Harness();
        var user = harness.Seed();
        var shared = user.Version;

        await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim First", shared), default);
        await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim Second", shared), default);

        var retried = await harness.Users.UpdateAsync(
            user.Id, Edit(user, "Kim Second", harness.Stored(user.Id).Version), default);

        Assert.True(retried.Succeeded, retried.Error);
        Assert.Equal("Kim Second", harness.Stored(user.Id).DisplayName);
    }

    [Fact]
    public async Task An_edit_that_carries_no_version_is_refused_rather_than_waved_through()
    {
        // An optional token is worse than none: it restores last-write-wins for any client that
        // forgets it, and nothing ever says so. Not a conflict, though — the request was simply
        // never one WM accepts, so the endpoint answers 400.
        using var harness = new Harness();
        var user = harness.Seed();

        var result = await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim Second", version: null), default);

        Assert.False(result.Succeeded);
        Assert.False(result.Conflict);
        Assert.Contains("did not carry a record version", result.Error);
        Assert.Equal("Kim Ops", harness.Stored(user.Id).DisplayName);
    }

    [Fact]
    public async Task A_missing_user_is_still_not_found_rather_than_a_conflict()
    {
        // The identity mirror of People's "scope check runs first": whether the record EXISTS is
        // settled before the token is looked at, so a token can never be used to probe for one.
        using var harness = new Harness();

        var result = await harness.Users.UpdateAsync(
            Guid.CreateVersion7(),
            new UpdateUserRequest("Ghost", "ghost@example.com", true, null, [], Guid.CreateVersion7()),
            default);

        Assert.False(result.Succeeded);
        Assert.False(result.Conflict);
        Assert.Equal("User not found.", result.Error);
    }

    [Fact]
    public async Task The_version_is_listed_with_the_user_and_changes_when_the_user_is_written()
    {
        // A token the users screen cannot read is a token it cannot echo — the list is what its
        // drawer opens from.
        using var harness = new Harness();
        var user = harness.Seed();

        var before = (await harness.Users.ListAsync(null, 1, 25, default)).Items.Single().Version;
        Assert.NotEqual(Guid.Empty, before);

        await harness.Users.UpdateAsync(user.Id, Edit(user, "Kim First", before), default);

        var after = (await harness.Users.ListAsync(null, 1, 25, default)).Items.Single().Version;
        Assert.NotEqual(before, after);
        Assert.Equal(harness.Stored(user.Id).Version, after);
    }

    private static UpdateUserRequest Edit(User user, string displayName, Guid? version) =>
        new(displayName, user.Email, user.IsActive, user.EmployeeId, [], version);

    private sealed class Harness : IDisposable
    {
        private readonly string _store = $"identity-{Guid.NewGuid():N}";

        public IdentityDbContext Db { get; }
        public UserManagementService Users { get; }

        public Harness()
        {
            Db = Context();
            Users = new UserManagementService(Db, new PasswordHasher<User>(), new SilentNotifier());
        }

        private IdentityDbContext Context() =>
            new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(_store).Options);

        public User Seed()
        {
            var user = new User
            {
                UserName = "kim.ops", Email = "kim@example.com", DisplayName = "Kim Ops", IsActive = true,
            };
            Db.Users.Add(user);
            Db.SaveChanges();
            return user;
        }

        /// <summary>
        /// What the store actually holds, read through a SECOND context over the same store. The
        /// service's own context still tracks the entity it tried to save, so reading through it
        /// would show the refused write's values and every assertion here would be a lie.
        /// </summary>
        public User Stored(Guid id)
        {
            using var db = Context();
            return db.Users.AsNoTracking().Single(u => u.Id == id);
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class SilentNotifier : IScopeChangeNotifier
    {
        public Task UserScopeChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
