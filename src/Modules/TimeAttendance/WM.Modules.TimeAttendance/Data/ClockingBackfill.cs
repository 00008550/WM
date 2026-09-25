using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using WM.Modules.TimeAttendance.Domain;
using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Data;

/// <summary>
/// Gives every day that already has punches its <see cref="Clocking"/> row (plan 002 P1). Runs on every
/// start, after <see cref="PunchLocalDateBackfill"/>, before the host serves a request.
///
/// <para>
/// <b>The day is the punch's frozen <see cref="Punch.LocalDate"/></b> (008 Q4) — never re-derived from
/// the instant. A punch at 01:00 in Tashkent is on the Tashkent day, not the UTC one, and a punch whose
/// day was moved by an audited recalculate (P8) is on the day it was moved to.
/// </para>
///
/// <para>
/// <b>Insert-if-absent, one statement per batch.</b> Each batch is a single
/// <c>INSERT … ON CONFLICT ("EmployeeId", "Date") DO NOTHING</c>, so:
/// idempotent (a second run finds nothing missing and writes nothing); safe with several instances
/// starting at once (the unique key absorbs the loser's insert instead of failing it — legacy survived
/// that race only by swallowing the exception, <c>ServiceTasks.cs:222</c>); and safe if interrupted (a
/// batch commits whole or not at all, and the next start picks up whatever is still missing). An
/// existing row is never touched — its origin and template stay what they were.
/// </para>
///
/// <para>
/// <b>Punches with no <see cref="Punch.LocalDate"/></b> get no day, because they have no day to give.
/// <see cref="PunchLocalDateBackfill"/> runs first and fills every such row (its resolver always
/// answers), so after a normal start there are none. If one remains, it is left exactly as it is, counted
/// and logged as a warning — never dropped, never guessed from UTC — and the next start that dates it
/// also gives it its day.
/// </para>
/// </summary>
public sealed class ClockingBackfill(
    TimeAttendanceDbContext db,
    IClock clock,
    ILogger<ClockingBackfill> logger)
{
    private const int BatchSize = 500;

    public sealed record Result(int Created, int AlreadyPresent, int UndatedPunches);

    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var created = 0;
        var absorbed = 0;
        while (true)
        {
            var missing = await db.Punches
                .Where(p => p.LocalDate != null
                            && !db.Clockings.Any(c => c.EmployeeId == p.EmployeeId && c.Date == p.LocalDate))
                .Select(p => new { p.EmployeeId, Date = p.LocalDate!.Value })
                .Distinct()
                .OrderBy(x => x.EmployeeId).ThenBy(x => x.Date)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (missing.Count == 0)
                break;

            var inserted = await InsertIfAbsentAsync(missing.Select(x => (x.EmployeeId, x.Date)).ToList(), ct);
            created += inserted;
            absorbed += missing.Count - inserted;
        }

        var undated = await db.Punches.CountAsync(p => p.LocalDate == null, ct);

        if (created > 0 || absorbed > 0)
            logger.LogInformation(
                "Clocking backfill: created {Created} day(s) from punches' frozen local dates; {Absorbed} were created concurrently by another instance",
                created, absorbed);
        if (undated > 0)
            logger.LogWarning(
                "Clocking backfill: {Undated} punch(es) have no local date and were given no day. They are kept unchanged and will get one on the start that dates them",
                undated);
        return new Result(created, absorbed, undated);
    }

    /// <summary>
    /// Creates a <see cref="ClockingOrigin.Backfill"/> row for each pair that has none, in one statement,
    /// and returns how many it created. A pair that already exists — including one inserted by another
    /// instance between the read and this write — is skipped by the unique key, not failed.
    /// </summary>
    public async Task<int> InsertIfAbsentAsync(IReadOnlyList<(Guid EmployeeId, DateOnly Date)> days, CancellationToken ct = default)
    {
        if (days.Count == 0)
            return 0;

        var entity = db.Model.FindEntityType(typeof(Clocking))!;
        var table = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());

        var now = clock.UtcNow;
        var sql = new StringBuilder();
        sql.Append("INSERT INTO ").Append(table)
            .Append(" (\"Id\", \"EmployeeId\", \"Date\", \"DayTemplateId\", \"Origin\", \"ExceptionsMuted\", \"CreatedAt\", \"Version\") VALUES ");
        var args = new List<object?>(days.Count * 6);
        for (var i = 0; i < days.Count; i++)
        {
            var n = args.Count;
            if (i > 0) sql.Append(", ");
            sql.Append($"({{{n}}}, {{{n + 1}}}, {{{n + 2}}}, NULL, {{{n + 3}}}, FALSE, {{{n + 4}}}, {{{n + 5}}})");
            args.Add(Guid.CreateVersion7());
            args.Add(days[i].EmployeeId);
            args.Add(days[i].Date);
            args.Add((int)ClockingOrigin.Backfill);
            args.Add(now);
            args.Add(Guid.CreateVersion7());
        }
        sql.Append(" ON CONFLICT (\"EmployeeId\", \"Date\") DO NOTHING");

        return await db.Database.ExecuteSqlAsync(FormattableStringFactory.Create(sql.ToString(), args.ToArray()), ct);
    }
}
