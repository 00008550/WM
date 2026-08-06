using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// The wiring half of the lockout change. <c>AccountLockoutTests</c> in the Identity module proves
/// <see cref="WM.Modules.Identity.Services.AuthService"/> reads the settings; these prove the host
/// binds them and refuses the two values that would turn a safety control into an outage.
///
/// <para>
/// Refused at composition, like the signing-key guard, and for the same reason: an administrator
/// who cannot sign in has no way to fix a setting that locked them out.
/// </para>
/// </summary>
public sealed class LockoutConfigurationTests
{
    [Fact]
    public void A_host_refuses_a_threshold_that_would_lock_on_the_first_failure()
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: With("Lockout:MaxFailedAttempts", "0")));

        Assert.Contains("Lockout__MaxFailedAttempts", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_refuses_a_lockout_that_expires_immediately()
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: With("Lockout:Duration", "00:00:00")));

        Assert.Contains("Lockout__Duration", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_composes_on_the_lenient_settings_a_public_demo_needs()
    {
        // The case this portion exists for: credentials published on purpose, so the first bored
        // visitor must not be able to lock the demo out of itself.
        await using var host = ApiTestHost.Compose(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Lockout:MaxFailedAttempts"] = "1000",
                ["Lockout:Duration"] = "00:01:00",
            });

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public async Task A_host_that_configures_no_lockout_at_all_still_composes()
    {
        // Every install that exists today. Absent must mean the shipped 5 / 15 minutes, not a
        // binding failure.
        await using var host = ApiTestHost.Compose();

        Assert.NotEmpty(host.Endpoints);
    }

    private static Dictionary<string, string?> With(string key, string value) =>
        new() { [key] = value };
}
