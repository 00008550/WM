using WM.Modules.People.Contracts;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.Modules.People.Services;
using WM.Modules.People.Tests.Endpoints;
using WM.SharedKernel.Security;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Modules.People.Tests.Services;

/// <summary>
/// 008 P4: <see cref="IEmployeeDirectory.ListEmployedAtLocalTodayAsync"/> answers each employee at
/// their <b>own</b> local today — the contract the live feed, presence and the demo seeders now ask,
/// instead of one UTC date for everybody. Same instants as <see cref="LocalTodayEndpointTests"/>.
/// </summary>
public sealed class EmployedAtLocalTodayTests
{
    private static readonly Guid Auckland = Guid.Parse("31111111-1111-1111-1111-111111111111");
    private static readonly Guid Honolulu = Guid.Parse("32222222-2222-2222-2222-222222222222");

    private static readonly DateOnly Jan15 = new(2026, 1, 15);
    private static readonly DateOnly Jan16 = new(2026, 1, 16);

    [Fact]
    public async Task At_noon_UTC_an_Auckland_leaver_of_the_15th_is_gone_and_a_Honolulu_one_is_still_employed()
    {
        var codes = await EmployedAt(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            [("AKL", Auckland, Jan15), ("HNL", Honolulu, Jan15)]);

        Assert.Equal(["HNL"], codes);
    }

    [Fact]
    public async Task After_UTC_rolls_over_a_Honolulu_employee_on_their_last_local_day_is_still_employed()
    {
        // 05:00 UTC on the 16th is 19:00 on the 15th in Honolulu. A UTC date would drop them.
        var codes = await EmployedAt(new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.Zero),
            [("HNL", Honolulu, Jan15), ("AKL", Auckland, Jan16)]);

        Assert.Equal(["AKL", "HNL"], codes);
    }

    [Fact]
    public async Task A_starter_whose_first_day_is_their_local_today_is_already_employed()
    {
        // Noon UTC on the 15th is the 16th in Auckland: an Auckland hire starting the 16th is in.
        var codes = await EmployedAt(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            [("AKL", Auckland, null)], from: Jan16);

        Assert.Equal(["AKL"], codes);
    }

    private static async Task<string[]> EmployedAt(
        DateTimeOffset now, (string Code, Guid Site, DateOnly? Until)[] employees, DateOnly? from = null)
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), db =>
        {
            db.Sites.AddRange(
                new Site { Id = Auckland, Name = "Auckland", TimeZone = "Pacific/Auckland" },
                new Site { Id = Honolulu, Name = "Honolulu", TimeZone = "Pacific/Honolulu" });
            foreach (var (code, site, until) in employees)
                db.Employees.Add(new Employee
                {
                    Code = code, FirstName = code, LastName = code, SiteId = site,
                    EmployedFrom = from ?? new DateOnly(2024, 1, 1), EmployedUntil = until,
                });
        });

        return host.Read(db =>
        {
            var directory = Directory(db, now);
            var scoped = directory.ListEmployedAtLocalTodayAsync().GetAwaiter().GetResult();
            var unscoped = directory.ListEmployedAtLocalTodayUnscopedAsync().GetAwaiter().GetResult();
            var codes = scoped.Select(e => e.Code).Order().ToArray();
            Assert.Equal(codes, unscoped.Select(e => e.Code).Order().ToArray());
            return codes;
        });
    }

    private static EmployeeDirectory Directory(PeopleDbContext db, DateTimeOffset now) => new(
        db, new AllScope(),
        new SiteZoneResolver(db, new InstallationZone(PeopleEndpointHost.InstallationDefault)),
        new SystemClock(new FixedTime(now)));

    private sealed class AllScope : IDataScopeResolver
    {
        public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) =>
            Task.FromResult(EffectiveDataScope.All());
        public Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(EffectiveDataScope.All());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
