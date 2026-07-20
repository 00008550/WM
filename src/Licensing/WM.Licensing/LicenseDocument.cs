namespace WM.Licensing;

/// <summary>
/// A WM license: signed JSON describing what a customer installation may use.
/// Verified offline with the embedded public key — no phone-home required.
/// </summary>
public sealed record LicenseDocument
{
    public required string LicenseId { get; init; }
    public required string Customer { get; init; }
    public required string Edition { get; init; }
    /// <summary>Feature flags, e.g. "scheduling", "accessControl", "plugin:payroll.demo".</summary>
    public required string[] Features { get; init; }
    /// <summary>Named limits, e.g. maxEmployees=500, maxSites=10.</summary>
    public required Dictionary<string, int> Limits { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    /// <summary>Days after NotAfter during which the product still runs with warnings.</summary>
    public int GraceDays { get; init; } = 14;

    public bool HasFeature(string feature) =>
        Features.Contains(feature, StringComparer.OrdinalIgnoreCase);

    public int LimitOf(string name, int fallback = int.MaxValue) =>
        Limits.TryGetValue(name, out var value) ? value : fallback;
}

public enum LicenseState
{
    Valid,
    NotYetValid,
    InGracePeriod,
    Expired,
    InvalidSignature,
    Malformed,
}

public sealed record LicenseValidation(LicenseState State, LicenseDocument? License, string? Message)
{
    public bool IsUsable => State is LicenseState.Valid or LicenseState.InGracePeriod;
}
