using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// 008 P3: an employment question asked without a date is answered at the employee's <b>local</b>
/// today — the calendar date on the wall clock of the zone their home site resolves to (008 P2) —
/// not at UTC's date, and not at one date for the whole response.
///
/// <para>
/// The instants are chosen so UTC's date disagrees with exactly one of the two sites. At
/// 2026-01-15 12:00 UTC it is already the 16th in Auckland (NZDT, +13) and still the 15th in
/// Honolulu (−10). At 2026-01-16 05:00 UTC it is the 16th in Auckland and still the 15th in
/// Honolulu, while UTC has moved on to the 16th. Every zone-dependent assertion reads the other way
/// if "today" is UTC's, or the installation default's (Ljubljana, +1).
/// </para>
/// </summary>
public sealed class LocalTodayEndpointTests
{
    private static readonly Guid Auckland = Guid.Parse("31111111-1111-1111-1111-111111111111");
    private static readonly Guid Honolulu = Guid.Parse("32222222-2222-2222-2222-222222222222");

    private static readonly DateOnly Jan15 = new(2026, 1, 15);
    private static readonly DateOnly Jan16 = new(2026, 1, 16);

    private static readonly DateTimeOffset NoonUtcJan15 = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FiveUtcJan16 = new(2026, 1, 16, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_response_answers_each_employee_at_their_own_local_today()
    {
        // Both employees' last day is the 15th. At noon UTC on the 15th Auckland has rolled over to
        // the 16th and Honolulu has not — so one is a leaver and the other is still at work, in the
        // same response. That difference is the whole point of the portion.
        await using var host = await StartAsync(NoonUtcJan15, db => Seed(db,
            ("AKL", Auckland, Jan15),
            ("HNL", Honolulu, Jan15)));

        var rows = await RowsAsync(host.ClientWith(WmPermissions.EmployeesView));

        Assert.Equal((int)EmployeeStatus.Leaver, rows["AKL"].GetProperty("status").GetInt32());
        Assert.False(rows["AKL"].GetProperty("isEmployed").GetBoolean());
        Assert.Equal("2026-01-16", rows["AKL"].GetProperty("asAt").GetString());

        Assert.Equal((int)EmployeeStatus.Active, rows["HNL"].GetProperty("status").GetInt32());
        Assert.True(rows["HNL"].GetProperty("isEmployed").GetBoolean());
        Assert.Equal("2026-01-15", rows["HNL"].GetProperty("asAt").GetString());
    }

    [Fact]
    public async Task An_Auckland_employee_whose_last_day_is_their_local_today_is_still_employed()
    {
        // Noon UTC on the 15th is 01:00 on the 16th in Auckland. The last day counts (legacy
        // IsActiveEmployment, 76.V5.22.0.0.sql:33-36), so the 16th is still a working day — and
        // it is the 16th, not UTC's 15th, that the row is asked about.
        await using var host = await StartAsync(NoonUtcJan15, db => Seed(db,
            ("AKL", Auckland, Jan16)));

        var row = (await RowsAsync(host.ClientWith(WmPermissions.EmployeesView)))["AKL"];

        Assert.Equal((int)EmployeeStatus.Active, row.GetProperty("status").GetInt32());
        Assert.Equal("2026-01-16", row.GetProperty("asAt").GetString());
    }

    [Fact]
    public async Task A_Honolulu_employee_whose_last_day_is_their_local_today_is_still_employed_after_UTC_rolls_over()
    {
        // The mirror: UTC has reached the 16th, Honolulu is at 19:00 on the 15th — their last day.
        // A UTC "today" would make them a leaver five hours early.
        await using var host = await StartAsync(FiveUtcJan16, db => Seed(db,
            ("HNL", Honolulu, Jan15)));

        var row = (await RowsAsync(host.ClientWith(WmPermissions.EmployeesView)))["HNL"];

        Assert.Equal((int)EmployeeStatus.Active, row.GetProperty("status").GetInt32());
        Assert.True(row.GetProperty("isEmployed").GetBoolean());
        Assert.Equal("2026-01-15", row.GetProperty("asAt").GetString());
    }

    [Fact]
    public async Task An_explicit_employedOn_still_wins_for_every_row()
    {
        await using var host = await StartAsync(NoonUtcJan15, db => Seed(db,
            ("AKL", Auckland, Jan15),
            ("HNL", Honolulu, Jan15)));

        var rows = await RowsAsync(host.ClientWith(WmPermissions.EmployeesView), "&employedOn=2026-01-15");

        foreach (var code in new[] { "AKL", "HNL" })
        {
            Assert.Equal((int)EmployeeStatus.Active, rows[code].GetProperty("status").GetInt32());
            Assert.Equal("2026-01-15", rows[code].GetProperty("asAt").GetString());
        }
    }

    [Fact]
    public async Task A_create_without_EmployedFrom_starts_on_the_sites_local_today()
    {
        // Noon UTC on the 15th: an Auckland hire starts on the 16th, a Honolulu hire on the 15th.
        await using var host = await StartAsync(NoonUtcJan15, db => Seed(db));
        var hr = host.ClientWith(WmPermissions.EmployeesManage);

        foreach (var (code, site) in new[] { ("NEWAKL", Auckland), ("NEWHNL", Honolulu) })
        {
            var response = await hr.PostAsJsonAsync("/api/employees", new EmployeeUpsertRequest(
                code, "New", "Hire", null, null, null, site, null, EmployedFrom: null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(Jan16, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Code == "NEWAKL").EmployedFrom));
        Assert.Equal(Jan15, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Code == "NEWHNL").EmployedFrom));
    }

    private static Task<PeopleEndpointHost> StartAsync(DateTimeOffset now, Action<PeopleDbContext> seed) =>
        PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), seed, time: new FixedTime(now));

    private static async Task<Dictionary<string, JsonElement>> RowsAsync(HttpClient client, string query = "")
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/employees?pageSize=200{query}");
        return page.GetProperty("items").EnumerateArray()
            .ToDictionary(r => r.GetProperty("code").GetString()!, r => r.Clone());
    }

    private static void Seed(PeopleDbContext db, params (string Code, Guid Site, DateOnly? Until)[] employees)
    {
        db.Sites.AddRange(
            new Site { Id = Auckland, Name = "Auckland", TimeZone = "Pacific/Auckland" },
            new Site { Id = Honolulu, Name = "Honolulu", TimeZone = "Pacific/Honolulu" });
        foreach (var (code, site, until) in employees)
            db.Employees.Add(new Employee
            {
                Code = code, FirstName = code, LastName = code, SiteId = site,
                EmployedFrom = new DateOnly(2024, 1, 1), EmployedUntil = until,
            });
    }

    /// <summary>A clock stopped at one instant.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
