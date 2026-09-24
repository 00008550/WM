using System.Globalization;
using System.Text.RegularExpressions;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// Parses the <c>timestamp</c> a client puts on a punch (008 P5). It arrives as a string, not a
/// <see cref="DateTimeOffset"/>, because System.Text.Json reads an offset-less ISO value as the
/// <i>server's</i> local time — the exact silent guess this portion removes — and the request could
/// no longer tell the two apart.
/// </summary>
public static partial class ClientTimestamp
{
    // ISO 8601 date-time ending in an explicit designator: Z, or ±hh:mm.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WithOffset();

    public static bool TryParse(string value, out DateTimeOffset instant, out string fault)
    {
        instant = default;
        if (!WithOffset().IsMatch(value))
        {
            fault = $"Punch timestamp '{value}' must be ISO 8601 with a UTC offset "
                    + "(e.g. '2026-09-25T08:00:00+05:00' or '2026-09-25T03:00:00Z'); "
                    + "a time without an offset is ambiguous and is not guessed.";
            return false;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant))
        {
            fault = $"Punch timestamp '{value}' is not a valid date and time.";
            return false;
        }

        fault = string.Empty;
        return true;
    }
}
