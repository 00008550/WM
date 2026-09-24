using Microsoft.EntityFrameworkCore;
using WM.Modules.TimeAttendance.Data;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// What allocation needs to know about one employee-day (legacy <c>dbo.Clockings</c>, three of its 249
/// columns): the day's own template (<c>DailyModelID</c>) and the punches already on it
/// (<c>BadgeTime1..12</c>, as instants and without the 12-slot ceiling).
/// </summary>
/// <param name="DayTemplateId"><c>null</c> when no template is assigned — no branch can read it.</param>
/// <param name="Punches">In any order; allocation orders them by time itself (edge case A8).</param>
public sealed record ClockingDay(DateOnly Date, Guid? DayTemplateId, IReadOnlyList<DateTimeOffset> Punches);

/// <summary>
/// The Clocking store as allocation sees it. <b>A port for plan 002</b>, which owns the Clocking
/// aggregate and the calendar job and replaces <see cref="PunchBackedClockingDays"/> with the real
/// store; plan 010 P2's rules are tested against a double of this interface.
/// </summary>
public interface IClockingDays
{
    /// <returns>The day, or <c>null</c> if no Clocking exists for it (legacy: no pre-generated row).</returns>
    Task<ClockingDay?> FindAsync(Guid employeeId, DateOnly date, CancellationToken ct);

    /// <summary>
    /// Edge case A10, <b>inverted</b>: legacy discards a swipe whose resolved day has no Clocking
    /// (<c>73.V5.19.0.0.sql:233-256</c>, <c>'Clock Record not found'</c>). WM creates the day instead —
    /// a punch is evidence and is never lost.
    /// </summary>
    /// <returns><c>true</c> if the day did not exist and was created — the fact plan 002 raises as a
    /// day-level exception ("day was not generated"); WM has no exception model before Phase 2.</returns>
    Task<bool> EnsureAsync(Guid employeeId, DateOnly date, CancellationToken ct);
}

/// <summary>
/// The interim store until plan 002 lands. A day is <b>implicit in its punches</b> — those whose frozen
/// <see cref="Domain.Punch.LocalDate"/> is that date — and has no template, because nothing assigns one
/// yet. So every allocation branch misses and allocation answers the local date, exactly as 008 P4 did:
/// P2 changes no production answer until 002 gives days their templates.
///
/// <para>
/// <see cref="EnsureAsync"/> has nothing to create: the punch row being written <i>is</i> the day's
/// evidence, so A10's invariant — no punch is lost — already holds.
/// </para>
/// </summary>
public sealed class PunchBackedClockingDays(TimeAttendanceDbContext db) : IClockingDays
{
    public async Task<ClockingDay?> FindAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var punches = await db.Punches.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.LocalDate == date)
            .Select(p => p.Timestamp)
            .ToListAsync(ct);
        return punches.Count == 0 ? null : new ClockingDay(date, null, punches);
    }

    public Task<bool> EnsureAsync(Guid employeeId, DateOnly date, CancellationToken ct) => Task.FromResult(false);
}
