using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.Modules.People.Services;
using WM.SharedKernel.Time;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// 007 P3 — one employee-code rule, edge cases 11–14 of plan 007.
///
/// <para>
/// <b>What these tests cannot prove, stated before anything else.</b> The in-memory provider does not
/// enforce unique indexes at all, so nothing in this file exercises <c>UX_Employees_Code_Lower</c>.
/// That the constraint exists, is unique, and is on <c>lower("Code")</c> is pinned by
/// <c>EmployeeCodeMigrationTests</c> — by inspecting the migration, not by executing it. Edge case 12
/// (two concurrent inserts) is tested here against a <see cref="CaseInsensitiveCodeConstraint"/>
/// <b>stand-in</b>: an interceptor that behaves like the index and raises the same SQLSTATE. It proves
/// the handler's half — both requests really do pass the pre-check, and the loser gets a 409 rather than
/// a 500 — and it proves nothing about Postgres.
/// </para>
/// </summary>
public sealed class EmployeeCodeEndpointTests
{
    private static readonly Guid Site = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Existing = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly DateOnly Hired = new(2024, 1, 15);

    [Fact]
    public async Task Edge11_a_code_differing_only_by_case_is_refused_on_create()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New("e1030"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, host.Read(db => db.Employees.Count()));
    }

    [Fact]
    public async Task Edge11_an_edit_onto_another_employees_code_in_a_different_case_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await host.PutEmployeeAsync(client, Other, New(" e1030 "));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("E2000", host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Other).Code));
    }

    [Fact]
    public async Task An_employee_may_recase_their_own_code()
    {
        // The exclusion of the record itself must survive the normalisation: E1030 -> e1030 on the
        // same person is not a collision with themselves.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await host.PutEmployeeAsync(client, Existing, New("e1030"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Edge12_two_concurrent_creates_differing_only_by_case_leave_one_row_and_a_409()
    {
        // Both requests are held at SaveChanges until BOTH have passed the pre-check — so this is the
        // race, not the sequential pair (which the pre-check alone answers, and which passed before P3).
        // The constraint they then meet is the stand-in; see the class remarks.
        var constraint = new CaseInsensitiveCodeConstraint(racers: 2);
        await using var host = await PeopleEndpointHost.StartAsync(
            EffectiveDataScope.All(), Seed, configureDb: o => o.AddInterceptors(constraint));
        var client = host.ClientWith(WmPermissions.EmployeesManage);
        constraint.Armed = true;

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/employees", New("N7000")),
            client.PostAsJsonAsync("/api/employees", New("n7000")));

        Assert.True(constraint.BothPassedThePreCheck, "the race was not staged: a request was refused before SaveChanges");
        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order().ToArray());
        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("already exists", await loser.Content.ReadAsStringAsync());
        Assert.Single(host.Read(db => db.Employees.AsNoTracking().Where(e => e.Code.ToLower() == "n7000").ToList()));
    }

    [Theory]
    [InlineData("E1030")]
    [InlineData("e1030")]
    [InlineData(" e1030 ")]
    public async Task Edge13_the_directory_resolves_a_code_in_any_case(string asPunched)
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);

        var found = host.Read(db => Directory(db).FindByCodeAsync(asPunched).GetAwaiter().GetResult());

        Assert.NotNull(found);
        Assert.Equal(Existing, found.Id);
    }

    [Fact]
    public async Task Edge14_leading_zeros_are_not_folded()
    {
        // Deliberate divergence from legacy's right('0000000000' + Code, 10) compare (open question 1).
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), db =>
        {
            Seed(db);
            db.Employees.Add(Person(Guid.NewGuid(), "42"));
        });
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New("0042"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var byPadded = host.Read(db => Directory(db).FindByCodeAsync("0042").GetAwaiter().GetResult());
        var byBare = host.Read(db => Directory(db).FindByCodeAsync("42").GetAwaiter().GetResult());
        Assert.NotNull(byPadded);
        Assert.NotNull(byBare);
        Assert.NotEqual(byPadded.Id, byBare.Id);
    }

    private static EmployeeDirectory Directory(PeopleDbContext db) => new(db, new AllScope(),
        new SiteZoneResolver(db, new InstallationZone(PeopleEndpointHost.InstallationDefault)),
        new SystemClock(TimeProvider.System));

    private static EmployeeUpsertRequest New(string code) =>
        new(code, "Grace", "Hopper", null, null, null, Site, null, Hired);

    private static Employee Person(Guid id, string code) => new()
    {
        Id = id, Code = code, FirstName = "Ada", LastName = "Lovelace", SiteId = Site, EmployedFrom = Hired,
    };

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = Site, Name = "North" });
        db.Employees.Add(Person(Existing, "E1030"));
        db.Employees.Add(Person(Other, "E2000"));
    }

    private sealed class AllScope : IDataScopeResolver
    {
        public Task<EffectiveDataScope> GetScopeAsync(CancellationToken ct = default) => Task.FromResult(EffectiveDataScope.All());
        public Task<EffectiveDataScope> GetScopeForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(EffectiveDataScope.All());
    }

    /// <summary>
    /// A <b>stand-in</b> for <c>UX_Employees_Code_Lower</c>, because the in-memory provider enforces no
    /// unique index. Serialises saves and refuses an insert or update whose code matches another row's
    /// case-insensitively, raising the <c>23505</c> Npgsql would. Written independently of
    /// <see cref="EmployeeCode"/> on purpose, so breaking the application's normalisation cannot also
    /// quietly break the constraint it is tested against.
    /// </summary>
    private sealed class CaseInsensitiveCodeConstraint(int racers) : SaveChangesInterceptor
    {
        private readonly Barrier _start = new(racers);
        private readonly SemaphoreSlim _serial = new(1, 1);
        private int _arrived;

        public bool Armed { get; set; }

        public bool BothPassedThePreCheck => Volatile.Read(ref _arrived) == racers;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            // The host's own seed goes through this interceptor too; it is not a racer.
            if (!Armed)
                return result;
            Interlocked.Increment(ref _arrived);
            // Every racer has cleared the pre-check before any of them is allowed to write.
            if (!_start.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("the other racer never reached SaveChanges");

            await _serial.WaitAsync(ct);
            var context = eventData.Context!;
            var writing = context.ChangeTracker.Entries<Employee>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .Select(e => (e.Entity.Id, e.Entity.Code)).ToList();
            var stored = await context.Set<Employee>().AsNoTracking().Select(e => new { e.Id, e.Code }).ToListAsync(ct);

            if (writing.Any(w => stored.Any(s => s.Id != w.Id && string.Equals(s.Code, w.Code, StringComparison.OrdinalIgnoreCase))))
            {
                _serial.Release();
                throw new DbUpdateException("stand-in unique violation", new Npgsql.PostgresException(
                    "duplicate key value violates unique constraint \"UX_Employees_Code_Lower\"", "ERROR", "ERROR", "23505"));
            }
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        {
            if (Armed) _serial.Release();
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
        {
            if (Armed && eventData.Exception is not DbUpdateException { InnerException: Npgsql.PostgresException })
                _serial.Release();
            return Task.CompletedTask;
        }
    }
}
