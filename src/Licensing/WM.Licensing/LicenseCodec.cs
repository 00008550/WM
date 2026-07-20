using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WM.Licensing;

/// <summary>
/// Signs and verifies license documents with ECDSA P-256.
/// Wire format: base64url(payloadJson) + "." + base64url(signature).
/// The private key never leaves the license generator/service.
/// </summary>
public static class LicenseCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static (string PrivateKeyPem, string PublicKeyPem) CreateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    public static string Sign(LicenseDocument license, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);

        var payload = JsonSerializer.SerializeToUtf8Bytes(license, JsonOptions);
        var signature = key.SignData(payload, HashAlgorithmName.SHA256);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    public static LicenseValidation Verify(string licenseKey, string publicKeyPem, DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow;

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 2)
            return new LicenseValidation(LicenseState.Malformed, null, "License key format is invalid.");

        byte[] payload;
        byte[] signature;
        try
        {
            payload = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return new LicenseValidation(LicenseState.Malformed, null, "License key encoding is invalid.");
        }

        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            return new LicenseValidation(LicenseState.InvalidSignature, null, "License signature check failed.");

        LicenseDocument? license;
        try
        {
            license = JsonSerializer.Deserialize<LicenseDocument>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            license = null;
        }
        if (license is null)
            return new LicenseValidation(LicenseState.Malformed, null, "License payload is invalid.");

        if (moment < license.NotBefore)
            return new LicenseValidation(LicenseState.NotYetValid, license, $"License starts {license.NotBefore:yyyy-MM-dd}.");
        if (moment > license.NotAfter.AddDays(license.GraceDays))
            return new LicenseValidation(LicenseState.Expired, license, $"License expired {license.NotAfter:yyyy-MM-dd}.");
        if (moment > license.NotAfter)
            return new LicenseValidation(LicenseState.InGracePeriod, license,
                $"License expired {license.NotAfter:yyyy-MM-dd}; grace period ends {license.NotAfter.AddDays(license.GraceDays):yyyy-MM-dd}.");

        return new LicenseValidation(LicenseState.Valid, license, null);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
