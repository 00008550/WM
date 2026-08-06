using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WM.Modules.Identity.Data;
using WM.Modules.Identity.Domain;
using WM.Modules.Identity.Services;
using Xunit;

namespace WM.Modules.Identity.Tests.Services;

/// <summary>
/// Lockout used to be two <c>const</c>s in <see cref="AuthService"/>: five failures, fifteen
/// minutes. 006 P3 makes them configuration, because the demo — whose credentials are published on
/// purpose — locks itself out of itself on the first credential-stuffing bot otherwise, and a
/// customer may want it stricter.
///
/// <para>
/// The first test is the one that matters most: it pins the shipped behaviour, so "configurable"
/// cannot quietly become "different". The rest prove the setting is actually read rather than
/// merely bound.
/// </para>
/// </summary>
public class AccountLockoutTests
{
    private const string Password = "Correct-Horse-1";

    [Fact]
    public void The_defaults_are_exactly_what_shipped_before_this_was_configurable()
    {
        var defaults = new AccountLockoutOptions();

        Assert.Equal(5, defaults.MaxFailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), defaults.Duration);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_and_four_do_not()
    {
        using var harness = new Harness();

        for (var attempt = 0; attempt < 4; attempt++)
            await harness.FailToSignInAsync();

        Assert.False(harness.User.IsLockedOut);

        await harness.FailToSignInAsync();

        Assert.True(harness.User.IsLockedOut);
    }

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_right_password()
    {
        using var harness = new Harness();

        for (var attempt = 0; attempt < 5; attempt++)
            await harness.FailToSignInAsync();

        var result = await harness.SignInAsync(Password);

        Assert.False(result.Succeeded);
        Assert.Contains("locked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_raised_threshold_changes_when_the_account_locks()
    {
        // The demo's setting. Five failures used to be the whole story; here they are not enough.
        using var harness = new Harness(new AccountLockoutOptions { MaxFailedAttempts = 10 });

        for (var attempt = 0; attempt < 9; attempt++)
            await harness.FailToSignInAsync();

        Assert.False(harness.User.IsLockedOut);
        Assert.True((await harness.SignInAsync(Password)).Succeeded);
    }

    [Fact]
    public async Task A_lowered_threshold_changes_it_too()
    {
        using var harness = new Harness(new AccountLockoutOptions { MaxFailedAttempts = 2 });

        await harness.FailToSignInAsync();
        Assert.False(harness.User.IsLockedOut);

        await harness.FailToSignInAsync();
        Assert.True(harness.User.IsLockedOut);
    }

    [Fact]
    public async Task The_lockout_lasts_as_long_as_configuration_says()
    {
        using var harness = new Harness(
            new AccountLockoutOptions { MaxFailedAttempts = 1, Duration = TimeSpan.FromMinutes(90) });

        var before = DateTimeOffset.UtcNow;
        await harness.FailToSignInAsync();

        Assert.NotNull(harness.User.LockedOutUntil);
        Assert.InRange(
            harness.User.LockedOutUntil.Value,
            before.AddMinutes(90),
            DateTimeOffset.UtcNow.AddMinutes(90));
    }

    [Fact]
    public async Task A_successful_sign_in_forgets_the_failures_before_it()
    {
        // Unchanged behaviour, pinned because this portion rewrote the arithmetic around it: a
        // counter that survived a good password would lock an account across days.
        using var harness = new Harness();

        for (var attempt = 0; attempt < 4; attempt++)
            await harness.FailToSignInAsync();

        Assert.True((await harness.SignInAsync(Password)).Succeeded);
        Assert.Equal(0, harness.User.FailedLoginAttempts);

        for (var attempt = 0; attempt < 4; attempt++)
            await harness.FailToSignInAsync();

        Assert.False(harness.User.IsLockedOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_threshold_below_one_is_a_fault_rather_than_a_way_to_disable_lockout(int threshold)
    {
        var fault = AccountLockoutOptions.DescribeFault(
            new AccountLockoutOptions { MaxFailedAttempts = threshold });

        Assert.NotNull(fault);
        Assert.Contains("Lockout__MaxFailedAttempts", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duration_of_zero_is_a_fault()
    {
        var fault = AccountLockoutOptions.DescribeFault(
            new AccountLockoutOptions { Duration = TimeSpan.Zero });

        Assert.NotNull(fault);
        Assert.Contains("Lockout__Duration", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_defaults_are_not_a_fault()
    {
        Assert.Null(AccountLockoutOptions.DescribeFault(new AccountLockoutOptions()));
    }

    private sealed class Harness : IDisposable
    {
        private readonly IdentityDbContext _db;

        public Harness(AccountLockoutOptions? lockout = null)
        {
            _db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"identity-{Guid.NewGuid():N}")
                .Options);

            var hasher = new PasswordHasher<User>();
            User = new User
            {
                UserName = "kim.ops",
                Email = "kim@example.com",
                DisplayName = "Kim Ops",
            };
            User.PasswordHash = hasher.HashPassword(User, Password);
            _db.Users.Add(User);
            _db.SaveChanges();

            var tokens = new TokenService(Options.Create(new JwtOptions
            {
                Issuer = "wm-tests",
                Audience = "wm-tests",
                SigningKey = new string('k', 32),
            }));

            Auth = new AuthService(
                _db,
                tokens,
                hasher,
                Options.Create(lockout ?? new AccountLockoutOptions()),
                NullLogger<AuthService>.Instance);
        }

        public AuthService Auth { get; }

        public User User { get; }

        public Task<AuthResult> SignInAsync(string password) =>
            Auth.LoginAsync(User.UserName, password, CancellationToken.None);

        public async Task FailToSignInAsync()
        {
            var result = await SignInAsync("not-the-password");
            Assert.False(result.Succeeded);
        }

        public void Dispose() => _db.Dispose();
    }
}
