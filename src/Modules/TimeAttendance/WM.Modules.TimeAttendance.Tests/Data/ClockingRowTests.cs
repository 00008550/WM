using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using Xunit;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Tests.Data;

/// <summary>
/// Plan 002 P1 — the Clocking row and its startup backfill. <b>On SQLite, not InMemory</b>: InMemory
/// does not enforce unique indexes, and <c>(EmployeeId, Date)</c> uniqueness is what these tests are
/// about — both the duplicate refusal and the backfill's <c>ON CONFLICT DO NOTHING</c> need a real key.
/// </summary>
public sealed class ClockingRowTests : IDisposable
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Site = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 6, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ClockingRowTests()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private TimeAttendanceDbContext NewDb() => new(new DbContextOptionsBuilder<TimeAttendanceDbContext>()
        .UseSqlite(_connection).Options);

    private ClockingBackfill Backfill(TimeAttendanceDbContext db) =>
        new(db, new SystemClock(new FixedTime(Now)), NullLogger<ClockingBackfill>.Instance);

    /// <summary>
    /// Every backfill run in this class is bounded. The backfill's loop ends only when its read finds
    /// nothing missing; a defect that makes the read and the insert disagree would otherwise hang the
    /// suite instead of failing it. <c>Task.Run</c> because SQLite completes synchronously — a spinning
    /// loop on the test thread would never yield to the timeout.
    /// </summary>
    private Task<ClockingBackfill.Result> RunBounded(TimeAttendanceDbContext db, ILogger<ClockingBackfill>? log = null) =>
        Task.Run(() => new ClockingBackfill(db, new SystemClock(new FixedTime(Now)),
                log ?? NullLogger<ClockingBackfill>.Instance).RunAsync())
            .WaitAsync(TimeSpan.FromSeconds(20));

    private static DateOnly Day(int month, int day) => new(2026, month, day);

    private static Punch PunchOn(Guid employee, DateTimeOffset at, DateOnly? localDate) => new()
    {
        EmployeeId = employee, EmployeeCode = "E", SiteId = Site, Timestamp = at, LocalDate = localDate,
        Direction = PunchDirection.In, Source = PunchSource.Terminal, ReceivedAt = at,
    };

    private async Task Seed(params Punch[] punches)
    {
        await using var db = NewDb();
        db.Punches.AddRange(punches);
        await db.SaveChangesAsync();
    }

    private async Task<List<Clocking>> Rows()
    {
        await using var db = NewDb();
        return (await db.Clockings.AsNoTracking().ToListAsync())
            .OrderBy(c => c.EmployeeId).ThenBy(c => c.Date).ToList();
    }

    [Fact]
    public async Task A_second_row_for_the_same_employee_and_day_is_refused_by_the_key()
    {
        await using (var db = NewDb())
        {
            db.Clockings.Add(new Clocking { EmployeeId = Ada, Date = Day(9, 1), CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using var second = NewDb();
        second.Clockings.Add(new Clocking { EmployeeId = Ada, Date = Day(9, 1), CreatedAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_day_with_no_punches_is_a_valid_row()
    {
        await using (var db = NewDb())
        {
            db.Clockings.Add(new Clocking { EmployeeId = Ada, Date = Day(9, 2), Origin = ClockingOrigin.Calendar, CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await Rows());
        Assert.Equal(Day(9, 2), row.Date);
        Assert.Null(row.DayTemplateId);
        Assert.False(row.ExceptionsMuted);
        Assert.Equal(ClockingOrigin.Calendar, row.Origin);
    }

    [Fact]
    public async Task Backfill_creates_one_row_per_employee_and_frozen_local_day()
    {
        // Ada: two punches on 1 Sep, one on 2 Sep. Bob: one on 1 Sep.
        await Seed(
            PunchOn(Ada, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)),
            PunchOn(Ada, new(2026, 9, 1, 13, 0, 0, TimeSpan.Zero), Day(9, 1)),
            PunchOn(Ada, new(2026, 9, 2, 4, 0, 0, TimeSpan.Zero), Day(9, 2)),
            PunchOn(Bob, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)));

        await using var db = NewDb();
        var result = await RunBounded(db);

        Assert.Equal(new ClockingBackfill.Result(3, 0, 0), result);
        var rows = await Rows();
        Assert.Equal([(Ada, Day(9, 1)), (Ada, Day(9, 2)), (Bob, Day(9, 1))], rows.Select(r => (r.EmployeeId, r.Date)));
        Assert.All(rows, r =>
        {
            Assert.Equal(ClockingOrigin.Backfill, r.Origin);
            Assert.Null(r.DayTemplateId);
            Assert.False(r.ExceptionsMuted);
            Assert.Equal(Now, r.CreatedAt);
        });
    }

    [Fact]
    public async Task Backfill_uses_the_frozen_local_date_not_the_utc_date()
    {
        // 01:00 in Tashkent (+05) on 10 Mar is 20:00 UTC on 9 Mar. The punch is frozen on the 10th.
        await Seed(PunchOn(Ada, new(2026, 3, 9, 20, 0, 0, TimeSpan.Zero), Day(3, 10)));

        await using var db = NewDb();
        await RunBounded(db);

        var row = Assert.Single(await Rows());
        Assert.Equal(Day(3, 10), row.Date);
    }

    [Fact]
    public async Task Backfill_twice_creates_nothing_the_second_time()
    {
        await Seed(
            PunchOn(Ada, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)),
            PunchOn(Bob, new(2026, 9, 3, 4, 0, 0, TimeSpan.Zero), Day(9, 3)));

        await using (var db = NewDb())
            Assert.Equal(2, (await RunBounded(db)).Created);
        var first = await Rows();

        await using (var db = NewDb())
            Assert.Equal(new ClockingBackfill.Result(0, 0, 0), await RunBounded(db));
        var second = await Rows();

        Assert.Equal(first.Select(r => (r.Id, r.EmployeeId, r.Date, r.Version)), second.Select(r => (r.Id, r.EmployeeId, r.Date, r.Version)));
    }

    [Fact]
    public async Task Backfill_leaves_an_existing_day_as_it_is_and_fills_only_the_missing_ones()
    {
        var template = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Clockings.Add(new Clocking
            {
                EmployeeId = Ada, Date = Day(9, 1), Origin = ClockingOrigin.Calendar, DayTemplateId = template,
                ExceptionsMuted = true, CreatedAt = Now.AddDays(-30),
            });
            await db.SaveChangesAsync();
        }
        await Seed(
            PunchOn(Ada, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)),
            PunchOn(Ada, new(2026, 9, 2, 4, 0, 0, TimeSpan.Zero), Day(9, 2)));

        await using (var db = NewDb())
            Assert.Equal(1, (await RunBounded(db)).Created);

        var rows = await Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal((ClockingOrigin.Calendar, (Guid?)template, true, Now.AddDays(-30)),
            (rows[0].Origin, rows[0].DayTemplateId, rows[0].ExceptionsMuted, rows[0].CreatedAt));
        Assert.Equal(ClockingOrigin.Backfill, rows[1].Origin);
    }

    [Fact]
    public async Task A_concurrent_insert_of_the_same_day_is_absorbed_by_the_key_not_failed()
    {
        // The race: another instance read "missing" too and inserted first. This instance's insert for
        // the same pair must neither throw nor duplicate — it creates only what is still absent.
        await using (var other = NewDb())
            Assert.Equal(1, await Backfill(other).InsertIfAbsentAsync([(Ada, Day(9, 1))]));

        await using var db = NewDb();
        Assert.Equal(1, await Backfill(db).InsertIfAbsentAsync([(Ada, Day(9, 1)), (Ada, Day(9, 2))]));

        Assert.Equal([Day(9, 1), Day(9, 2)], (await Rows()).Select(r => r.Date));
    }

    [Fact]
    public async Task A_punch_without_a_local_date_gets_no_day_is_kept_and_is_counted()
    {
        var undated = PunchOn(Bob, new(2026, 9, 4, 4, 0, 0, TimeSpan.Zero), null);
        await Seed(PunchOn(Ada, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)), undated);

        await using (var db = NewDb())
            Assert.Equal(new ClockingBackfill.Result(1, 0, 1), await RunBounded(db));

        Assert.Equal([Ada], (await Rows()).Select(r => r.EmployeeId));
        await using var check = NewDb();
        var kept = await check.Punches.AsNoTracking().SingleAsync(p => p.Id == undated.Id);
        Assert.Null(kept.LocalDate);
    }

    [Fact]
    public async Task Backfill_works_across_several_batches()
    {
        // BatchSize is 500: 1,200 distinct days force three batches and prove the loop terminates.
        var start = new DateTimeOffset(2023, 1, 1, 9, 0, 0, TimeSpan.Zero);
        await Seed(Enumerable.Range(0, 1200)
            .Select(i => PunchOn(Ada, start.AddDays(i), DateOnly.FromDateTime(start.AddDays(i).UtcDateTime)))
            .ToArray());

        await using (var db = NewDb())
            Assert.Equal(1200, (await RunBounded(db)).Created);
        Assert.Equal(1200, (await Rows()).Count);
    }

    [Fact(Timeout = 15_000)]
    public async Task When_the_insert_and_the_read_disagree_the_backfill_stops_and_says_so_instead_of_spinning()
    {
        // Force the disagreement the stall guard exists for: every insert is silently discarded, so the
        // read keeps returning the same missing days. Before the guard this looped forever (host boot
        // hung); now it must return promptly, report them, log an error, and leave the punches alone.
        await Seed(
            PunchOn(Ada, new(2026, 9, 1, 4, 0, 0, TimeSpan.Zero), Day(9, 1)),
            PunchOn(Bob, new(2026, 9, 2, 4, 0, 0, TimeSpan.Zero), Day(9, 2)));
        await using (var db = NewDb())
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER swallow_clockings BEFORE INSERT ON \"Clockings\" BEGIN SELECT RAISE(IGNORE); END;");

        var log = new ListLogger();
        await using var run = NewDb();
        var result = await RunBounded(run, log);

        Assert.Equal(new ClockingBackfill.Result(0, 0, 0, 2), result);
        Assert.Empty(await Rows());
        var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains($"{Ada}/2026-09-01", error.Message);
        Assert.Contains($"{Bob}/2026-09-02", error.Message);
        await using var check = NewDb();
        Assert.Equal(2, await check.Punches.CountAsync());
    }

    private sealed class ListLogger : ILogger<ClockingBackfill>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
