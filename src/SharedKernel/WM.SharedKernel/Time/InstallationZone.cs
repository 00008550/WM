namespace WM.SharedKernel.Time;

/// <summary>
/// The zone an installation measures its days in when nothing more specific says otherwise —
/// legacy's <c>SoftwareMainOptions.SystemTimeZone</c>, but IANA and validated (plan 008 P1).
/// Read from configuration key <see cref="ConfigurationKey"/> and checked while the host
/// composes; a host with a missing or unknown value does not start.
///
/// <para>
/// In SharedKernel rather than the API host because plan 008 P2's resolver in People needs it as
/// the last link of site → parent chain → installation default, and a module cannot reference
/// the host. It is a value, not behaviour.
/// </para>
/// </summary>
public sealed record InstallationZone(ZoneId Zone)
{
    public const string ConfigurationKey = "Time:InstallationZone";
}
