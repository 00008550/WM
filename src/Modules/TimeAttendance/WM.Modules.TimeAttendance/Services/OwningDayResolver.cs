using WM.SharedKernel.Time;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>
/// <i>"Which working day does this punch belong to?"</i> — the one named seam every punch passes
/// through when it is recorded (plan 008 P4). The answer is <b>frozen on the punch</b>
/// (<see cref="Domain.Punch.LocalDate"/>, user decision 2026-09-24, open question 4) and read back
/// from there; nothing re-derives it at read time, so a later zone edit or tzdata update never moves
/// a punch to another day.
///
/// <para>
/// <b>Today's body is the naive answer:</b> the calendar date on the wall clock of the employee's
/// home-site zone. Legacy's allocation (<c>dbo.ProcessQueryGetClockingForSwipe</c>,
/// <c>37.V3.6.1.0.sql:748-868</c>) decides it with the day template instead, in five branches:
/// <c>NightShiftEndTime</c> (a punch before it belongs to yesterday), <c>ShiftToSunday</c> /
/// <c>OffsetTransactionToNextDay</c> with <c>NightShiftStartTime</c> (a punch after it belongs to
/// tomorrow), <c>InterswipeIntervalToMoveToYesterday</c> (a punch within that span of yesterday's first
/// punch belongs to yesterday), the master-template override, and shift matching. <b>Plan 002 owns
/// all five</b> and replaces this body; the input it needs — the instant and the zone its wall-clock
/// times are measured in — is what this signature already carries.
/// </para>
/// </summary>
public interface IOwningDayResolver
{
    DateOnly Resolve(DateTimeOffset instant, ZoneId zone);
}

/// <inheritdoc cref="IOwningDayResolver"/>
public sealed class LocalCalendarDayResolver : IOwningDayResolver
{
    public DateOnly Resolve(DateTimeOffset instant, ZoneId zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.DateAt(instant);
    }
}
