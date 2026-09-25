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
///
/// <para>
/// <b>It stops rather than spins, and it does not fail the boot.</b> If a batch inserts nothing and the
/// next read returns the same days, the read and the insert disagree about the key — a defect. The run
/// logs an error naming a sample of those days, reports them as <see cref="Result.Unresolved"/>, and
/// returns. Not a throw: no punch is lost or changed either way (the punch is the record; the Clocking
/// row is derived from it and every later start retries), and in P1 nothing reads the row yet, whereas
/// a throw here would stop every instance from serving punches at all over a derived table. The error
/// log is the signal.
/// </para>
/// </summary>
public sealed class ClockingBackfill(
    TimeAttendanceDbContext db,
    IClock clock,
    ILogger<ClockingBackfill> logger)
{
    private const int BatchSize = 500;

    private const int SampleSize = 10;

    /// <param name="Unresolved">Days the backfill read as missing but could not create — the stall
    /// guard's count. Zero on every healthy run.</param>
    public sealed record Result(int Created, int AlreadyPresent, int UndatedPunches, int Unresolved = 0);

    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var created = 0;
        var absorbed = 0;
        var unresolved = 0;
        List<(Guid EmployeeId, DateOnly Date)>? previous = null;
        var previousInserted = -1;
        while (true)
        {
            var missing = (await db.Punches
                .Where(p => p.LocalDate != null
                            && !db.Clockings.Any(c => c.EmployeeId == p.EmployeeId && c.Date == p.LocalDate))
                .Select(p => new { p.EmployeeId, Date = p.LocalDate!.Value })
                .Distinct()
                .OrderBy(x => x.EmployeeId).ThenBy(x => x.Date)
                .Take(BatchSize)
                .ToListAsync(ct))
                .Select(x => (x.EmployeeId, x.Date)).ToList();
            if (missing.Count == 0)
                break;

            // No-progress guard (002 P1 review). The loop ends only when the read finds nothing missing,
            // so it relies on the read and the insert agreeing on what "this day exists" means. If they
            // ever disagree — an insert that writes a different key than the read looks for, a filter
            // that stops matching the unique index — the same batch comes back forever and the host
            // never finishes booting, silently. A batch that inserted nothing and is read back unchanged
            // is exactly that disagreement: report it and stop.
            if (previousInserted == 0 && previous is not null && previous.SequenceEqual(missing))
            {
                // The previous pass counted these as absorbed by another instance; they were not.
                absorbed -= missing.Count;
                unresolved = missing.Count;
                logger.LogError(
                    "Clocking backfill stopped without progress: {Count} day(s) read as missing could not be created, and re-reading returned the same ones. The read and the insert disagree about the (EmployeeId, Date) key; this is a defect, not data. First {SampleSize}: {Sample}",
                    missing.Count, Math.Min(SampleSize, missing.Count),
                    string.Join(", ", missing.Take(SampleSize).Select(d => $"{d.EmployeeId}/{d.Date:yyyy-MM-dd}")));
                break;
            }

            var inserted = await InsertIfAbsentAsync(missing, ct);
            created += inserted;
            absorbed += missing.Count - inserted;
            previous = missing;
            previousInserted = inserted;
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
        return new Result(created, absorbed, undated, unresolved);
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
