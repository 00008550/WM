namespace WM.Modules.Identity.Services;

/// <summary>
/// The password-less sign-in a development host offers to agents verifying UI work (plan 011 P9).
///
/// <para>
/// This is a password bypass by design, so its entire safety is two gates and a guard. The route
/// is mapped only when the host environment is Development <em>and</em> <see cref="Enabled"/> is
/// true — either alone and it does not exist (404, not 401). And <see cref="Enabled"/> set in any
/// other environment stops the host booting, so a copied setting is a crash with a readable
/// message rather than a silently open door that happens to be closed by the environment name.
/// </para>
/// </summary>
public sealed class DevSignInOptions
{
    public const string SectionName = "DevSignIn";

    public bool Enabled { get; set; }

    /// <summary>
    /// One line saying why this host must not start, or <c>null</c> when it may — the same shape
    /// as <see cref="JwtOptions.DescribeSigningKeyFault"/>, read at composition for the same reason.
    /// </summary>
    public static string? DescribeFault(DevSignInOptions options, bool isDevelopment)
    {
        if (options.Enabled && !isDevelopment)
            return $"{SectionName}:Enabled is true outside Development. It turns on a sign-in that "
                   + "needs no password, and exists only for local agent verification. Remove it "
                   + "(env: DevSignIn__Enabled) from this host's configuration.";

        return null;
    }

    /// <summary>Both gates. The one predicate the endpoint mapping asks.</summary>
    public static bool ShouldMap(DevSignInOptions options, bool isDevelopment) =>
        options.Enabled && isDevelopment;
}
