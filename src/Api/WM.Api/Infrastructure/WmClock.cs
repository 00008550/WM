using WM.SharedKernel.Time;

namespace WM.Api.Infrastructure;

/// <summary>
/// Registers the clock and the installation zone, and refuses to compose without a real zone
/// (plan 008 P1). Same shape as 006 P2's signing-key guard: the fault is raised while services
/// register — before <c>Program.cs</c> opens a database — with one line naming the setting and
/// the environment variable that fixes it.
///
/// <para>
/// <b>There is no fallback, in any environment.</b> Legacy fails open to the server clock in seven
/// places when its zone is missing or unparseable (<c>docs/TLW-TIME-MODEL.md</c> §7). Substituting
/// UTC here would be the same defect with a different default. Development gets its value from
/// <c>appsettings.Development.json</c>; <c>appsettings.json</c>, which ships in the image,
/// deliberately carries none, so every deployment decides.
/// </para>
/// </summary>
public static class WmClock
{
    public const string EnvironmentVariable = "Time__InstallationZone";

    public static IServiceCollection AddWmClock(
        this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration[InstallationZone.ConfigurationKey];
        if (!ZoneId.TryParse(configured, out var zone, out var fault))
            throw new InvalidOperationException(
                $"{InstallationZone.ConfigurationKey} is not usable: {fault} Set it (environment "
                + $"variable {EnvironmentVariable}) to the IANA zone this installation measures its "
                + "days in, e.g. 'Europe/Ljubljana'. There is no default.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(new InstallationZone(zone));
        return services;
    }
}
