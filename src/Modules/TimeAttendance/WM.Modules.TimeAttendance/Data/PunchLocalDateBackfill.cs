using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WM.Modules.People.Contracts;

namespace WM.Modules.TimeAttendance.Data;

/// <summary>
/// Freezes a local day onto every punch recorded before 008 P4 put one on the row.
///
/// <para>
/// <b>Why here and not in the migration.</b> The zone lives in People (<c>people."Sites"</c>, resolved
/// site → ancestor → installation default by <see cref="ISiteTimeZones"/>), and the installation
/// default is configuration the database has never seen. A migration that joined into People's schema
/// would be the cross-module read invariant 1 forbids, and it still could not know the default. So the
/// migration adds the columns nullable, and this runs after migrations on every start — before the
/// host serves a request — asking the contract, exactly as a new punch does.
/// </para>
///
/// <para>
/// Each row is resolved in the zone of the site <b>stored on the punch</b> — the employee's home site
/// when it was recorded (Q2 (a)) — not wherever they work now. Every null row gets a value: the
/// resolver always answers, falling back to the installation default, so nothing is skipped. Once
/// filled, a row is never touched again (Q4 = freeze). Idempotent; a no-op when nothing is null.
/// </para>
///
/// <para>
/// <b>Plain calendar date, deliberately — not the allocation seam</b> (plan 010 P2). This fills rows
/// recorded before any day was frozen, and it answered with the local calendar date when 008 P4
/// shipped. Allocating now would date those rows by templates and neighbouring punches as they stand
/// today, not as they stood when the punch arrived. Punches already frozen by it are <b>not</b>
/// re-dated by P2: moving a frozen punch is an explicit, audited recalculate, never a side effect.
/// </para>
/// </summary>
public sealed class PunchLocalDateBackfill(
    TimeAttendanceDbContext db,
    ISiteTimeZones zones,
    ILogger<PunchLocalDateBackfill> logger)
{
    private const int BatchSize = 1000;

    /// <returns>How many punches were given a local day.</returns>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var total = 0;
        while (true)
        {
            var batch = await db.Punches
                .Where(p => p.LocalDate == null)
                .OrderBy(p => p.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var zoneBySite = await zones.ForSitesAsync(batch.Select(p => p.SiteId).Distinct(), ct);
            foreach (var punch in batch)
            {
                var zone = zoneBySite[punch.SiteId].Zone;
                punch.LocalDate = zone.DateAt(punch.Timestamp);
                punch.LocalZone = zone.Id;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
        }

        if (total > 0)
            logger.LogInformation("Froze a local day onto {Count} punch(es) recorded before 008 P4", total);
        return total;
    }
}
