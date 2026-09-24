using System.Text.RegularExpressions;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// Plan 008 P1: legacy's <c>AvoidDirectTimeUsageAnalyzer</c> without the analyzer infrastructure.
/// Every raw clock read in non-test <c>src/**/*.cs</c> is counted per file and compared against
/// <see cref="Allowed"/>, in the shape of <see cref="EndpointAuthorizationInventoryTests"/>: a new
/// read fails <em>by file name</em> until someone lists it deliberately, and an entry whose reads
/// have gone fails too, so the list can only shrink honestly.
///
/// <para>
/// The sanctioned way to read now is <c>WM.SharedKernel.Time.IClock</c>. Plans 008 P2–P5 empty
/// the entries marked <b>day</b>; the ones marked <b>stamp</b> are instants that are legitimately
/// UTC and move to <c>IClock</c> for testability, not correctness.
/// </para>
/// </summary>
public sealed partial class RawClockReadInventoryTests
{
    /// <summary>
    /// Repo-relative path (forward slashes) → how many raw reads it holds. Measured 2026-09-24:
    /// 27 reads in 13 files (plan 008 counted 24 in 12 on 2026-08-14; DemoUserSeeder, added by
    /// 011 P9's dev path, and three further reads arrived after it was written).
    /// </summary>
    private static readonly Dictionary<string, int> Allowed = new(StringComparer.Ordinal)
    {
        // stamp — the AuditableEntity CreatedAt default.
        ["src/SharedKernel/WM.SharedKernel/Domain/Entity.cs"] = 1,
        // stamp — licence validity instant, already injectable via its `now` parameter.
        ["src/Licensing/WM.Licensing/LicenseCodec.cs"] = 1,
        // stamp — lockout, CreatedAt, refresh-token expiry.
        ["src/Modules/Identity/WM.Modules.Identity/Domain/User.cs"] = 3,
        // stamp — lockout start and token revocation.
        ["src/Modules/Identity/WM.Modules.Identity/Services/AuthService.cs"] = 4,
        // stamp — UpdatedAt.
        ["src/Modules/Identity/WM.Modules.Identity/Services/SecurityGroupService.cs"] = 1,
        // stamp — token issue and refresh expiry.
        ["src/Modules/Identity/WM.Modules.Identity/Services/TokenService.cs"] = 2,
        // stamp — UpdatedAt and token revocation.
        ["src/Modules/Identity/WM.Modules.Identity/Services/UserManagementService.cs"] = 2,
        // day — demo EmployedFrom, a date taken from UTC.
        ["src/Modules/People/WM.Modules.People/Data/PeopleSeeder.cs"] = 1,
        // stamp — UpdatedAt. (Today(), the day read, went in 008 P3.)
        ["src/Modules/People/WM.Modules.People/PeopleModule.cs"] = 1,
        // day — demo punches laid on UTC days (008 P4).
        ["src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Data/PunchSeeder.cs"] = 2,
        // stamp — punch default instant and future guard (008 P5); day — employed-on dates (008 P4 — needs a per-employee local-today contract).
        ["src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Services/PunchService.cs"] = 5,
        // day — timesheet default "to" (008 P4).
        ["src/Modules/TimeAttendance/WM.Modules.TimeAttendance/TimeAttendanceModule.cs"] = 2,
        // day — the employed-on date for demo users (008 P4).
        ["src/Api/WM.Api/Infrastructure/DemoUserSeeder.cs"] = 1,
    };

    [Fact]
    public void Every_raw_clock_read_in_src_is_one_somebody_listed()
    {
        var root = RepositoryRoot();
        var found = Count(ProductSources(root)
            .Select(path => (Relative(root, path), File.ReadAllText(path))));

        var violations = Violations(found, Allowed);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void A_new_raw_read_in_an_unlisted_file_fails_by_name()
    {
        var found = Count([("src/Modules/X/New.cs", "var t = DateTimeOffset.UtcNow;")]);

        var violation = Assert.Single(Violations(found, new Dictionary<string, int>()));
        Assert.Contains("src/Modules/X/New.cs", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_raw_read_in_a_listed_file_fails_too()
    {
        var found = Count([("a.cs", "DateTime.UtcNow; DateTime.Now;")]);

        Assert.Single(Violations(found, new Dictionary<string, int> { ["a.cs"] = 1 }));
    }

    [Fact]
    public void A_listed_file_that_no_longer_reads_the_clock_is_a_stale_entry()
    {
        var found = Count([("a.cs", "var clean = clock.UtcNow;")]);

        var violation = Assert.Single(Violations(found, new Dictionary<string, int> { ["a.cs"] = 1 }));
        Assert.Contains("stale", violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.Today")]
    [InlineData("DateTime.UtcNow")]
    [InlineData("DateTimeOffset.UtcNow")]
    [InlineData("DateTimeOffset.Now")]
    [InlineData("System.DateTime.UtcNow")]
    public void Every_spelling_is_seen(string read)
    {
        Assert.Equal(1, Count([("a.cs", $"var x = {read};")])["a.cs"]);
    }

    [Theory]
    [InlineData("clock.UtcNow")]
    [InlineData("time.GetUtcNow()")]
    [InlineData("MyDateTime.UtcNow")]
    [InlineData("DateTime.UtcNowish")]
    public void What_is_not_a_raw_read_is_not_counted(string text)
    {
        Assert.False(Count([("a.cs", text)]).ContainsKey("a.cs"));
    }

    [GeneratedRegex(@"(?<![\w])DateTime(Offset)?\.(Now|UtcNow|Today)(?!\w)")]
    private static partial Regex RawRead();

    private static Dictionary<string, int> Count(IEnumerable<(string Path, string Text)> files) =>
        files
            .Select(f => (f.Path, Reads: RawRead().Matches(f.Text).Count))
            .Where(f => f.Reads > 0)
            .ToDictionary(f => f.Path, f => f.Reads, StringComparer.Ordinal);

    private static List<string> Violations(
        IReadOnlyDictionary<string, int> found, IReadOnlyDictionary<string, int> allowed)
    {
        List<string> violations = [];
        foreach (var (path, reads) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var listed = allowed.GetValueOrDefault(path);
            if (reads > listed)
                violations.Add($"{path}: {reads} raw clock read(s), {listed} allowed — use IClock.");
        }
        foreach (var (path, listed) in allowed.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            var reads = found.GetValueOrDefault(path);
            if (reads < listed)
                violations.Add($"{path}: stale entry — {listed} allowed, {reads} found. Lower it.");
        }
        return violations;
    }

    private static IEnumerable<string> ProductSources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var parts = Relative(root, path).Split('/');
                return !parts.Contains("bin") && !parts.Contains("obj")
                       && !parts.Any(p => p.EndsWith(".Tests", StringComparison.Ordinal));
            });

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WM.sln")))
                return dir.FullName;
        throw new InvalidOperationException("WM.sln not found above " + AppContext.BaseDirectory);
    }
}
