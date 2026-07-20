using System.Text.Json;
using WM.Licensing;

// WM license generator CLI.
//   keygen                      → writes wm-license-private.pem / wm-license-public.pem
//   sign <private.pem> <spec.json>  → prints signed license key
//   verify <public.pem> <key-file>  → validates and prints the license

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (command)
{
    case "keygen":
    {
        var (privatePem, publicPem) = LicenseCodec.CreateKeyPair();
        File.WriteAllText("wm-license-private.pem", privatePem);
        File.WriteAllText("wm-license-public.pem", publicPem);
        Console.WriteLine("Wrote wm-license-private.pem (KEEP SECRET) and wm-license-public.pem.");
        break;
    }
    case "sign" when args.Length >= 3:
    {
        var privatePem = File.ReadAllText(args[1]);
        var spec = JsonSerializer.Deserialize<LicenseDocument>(
            File.ReadAllText(args[2]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Could not parse license spec.");
        Console.WriteLine(LicenseCodec.Sign(spec, privatePem));
        break;
    }
    case "verify" when args.Length >= 3:
    {
        var publicPem = File.ReadAllText(args[1]);
        var result = LicenseCodec.Verify(File.ReadAllText(args[2]), publicPem);
        Console.WriteLine($"State: {result.State}");
        if (result.Message is not null) Console.WriteLine(result.Message);
        if (result.License is not null)
            Console.WriteLine(JsonSerializer.Serialize(result.License, new JsonSerializerOptions { WriteIndented = true }));
        return result.IsUsable ? 0 : 1;
    }
    default:
        Console.WriteLine("""
            WM License Generator
              keygen                          create a new signing key pair
              sign <private.pem> <spec.json>  sign a license spec, prints the key
              verify <public.pem> <key-file>  verify a license key file
            """);
        break;
}

return 0;
