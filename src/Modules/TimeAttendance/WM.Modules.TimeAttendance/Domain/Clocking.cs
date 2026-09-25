using WM.SharedKernel.Domain;

namespace WM.Modules.TimeAttendance.Domain;

/// <summary>
/// Who created a <see cref="Clocking"/> row — system provenance, not audit (plan 002, Target design:
/// system writes carry provenance, human writes are journalled from P4). Stored as its integer.
/// </summary>
public enum ClockingOrigin
{
    /// <summary>Generated ahead of time by the calendar (002 P2).</summary>
    Calendar = 0,

    /// <summary>Created because a punch arrived for a day the calendar had not generated (002 P3, G6).</summary>
    Punch = 1,

    /// <summary>Created at startup for a day that already had punches before the Clocking existed (002 P1).</summary>
    Backfill = 2,

    /// <summary>Created by a person (a later portion's endpoint).</summary>
    Manual = 3,
}

/// <summary>
/// The working day of one employee — legacy <c>dbo.Clockings</c> (249 columns), unique on
/// <c>(EmployeeId, Date)</c> (<c>28.V2.2.0.sql:6411</c>). Plan 002 P1 lands the row only: the day, its
/// planned template and its provenance. Counters (P7a), day results (P7b) and exceptions (P5) arrive
/// later.
///
/// <para>
/// <b>Punches are not foreign-keyed to it.</b> A punch says which day it is on through its frozen
/// <see cref="Punch.LocalDate"/> (008 Q4); the Clocking is the day, joined by
/// <c>(EmployeeId, LocalDate)</c>. <see cref="EmployeeId"/> is People's id — indexed, never a foreign
/// key (invariant 1).
/// </para>
/// <para>
/// <b><see cref="DayTemplateId"/> has no foreign key either, deliberately.</b> A day whose template has
/// since been deleted must stay representable so it can be reported (edge case S3, P5), and
/// <c>null</c> means "no planned template" — never a system default (the Invert of legacy's
/// <c>SYSTEM_DAILY_MODEL_CODE</c> fallback).
/// </para>
/// </summary>
public sealed class Clocking : Entity
{
    public Guid EmployeeId { get; set; }

    /// <summary>The employee's local calendar day, in the same terms as <see cref="Punch.LocalDate"/>.</summary>
    public DateOnly Date { get; set; }

    public Guid? DayTemplateId { get; set; }

    public ClockingOrigin Origin { get; set; }

    /// <summary>Legacy <c>Clockings.ShouldHideExceptions</c> — a flag the reader applies; it hides nothing
    /// from the API (P5).</summary>
    public bool ExceptionsMuted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency token, written by the entity like
    /// <see cref="AuditableEntity.Version"/> and for the same reason (never <c>xmin</c>). Enforced by the
    /// first human write (P4); P1 only stores it.</summary>
    public Guid Version { get; set; } = Guid.CreateVersion7();
}
