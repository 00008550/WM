using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.SharedKernel.Events;

namespace WM.Modules.TimeAttendance.Services;

public sealed record PunchResult(Punch? Punch, string? Error)
{
    public TResult Match<TResult>(Func<Punch, TResult> onSuccess, Func<string, TResult> onError) =>
        Punch is not null ? onSuccess(Punch) : onError(Error ?? "Unknown error.");
}

public sealed record LivePresenceEntry(
    Guid EmployeeId, string EmployeeCode, string EmployeeName, string? JobTitle,
    Guid SiteId, DateTimeOffset Since);

public sealed record LivePresence(int PresentCount, int ActiveEmployees, IReadOnlyList<LivePresenceEntry> Present);

public sealed record TimesheetDay(DateOnly Date, IReadOnlyList<TimesheetInterval> Intervals, double TotalHours);
public sealed record TimesheetInterval(DateTimeOffset In, DateTimeOffset? Out, double? Hours);

public sealed class PunchService(
    TimeAttendanceDbContext db,
    IEmployeeDirectory employees,
    IEventStreamProducer eventStream,
    IOptions<PunchDeduplicationOptions> dedupeOptions)
{
    // A caller must not be able to tell "this code exists but is not employed" from "this code does
    // not exist" — either answer would let them probe the roster. Both return this one message, so
    // the boundary is not an enumeration oracle (007 P2).
    private static PunchResult NotPunchable(string code) =>
        new(null, $"No employee is available to record a punch for '{code}'.");

    public async Task<PunchResult> RecordAsync(RecordPunchRequest request, Guid? recordedBy, CancellationToken ct)
    {
        var employee = await employees.FindByCodeAsync(request.EmployeeCode, ct);
        if (employee is null)
            return NotPunchable(request.EmployeeCode);

        var timestamp = request.Timestamp ?? DateTimeOffset.UtcNow;
        if (timestamp > DateTimeOffset.UtcNow.AddMinutes(5))
            return new PunchResult(null, "Punch timestamp cannot be in the future.");

        // Fail closed at the punch's OWN date, not today: only someone who may work on the day the
        // punch is dated may record it. This is the same predicate the live feed and presence apply
        // (!IsSuspended && employed on the date), so WM can no longer accept a punch it would then
        // refuse to display. A back-dated punch into an employed period is allowed; the same punch
        // after the leave date — or for a suspended employee — is refused, with the same message as
        // an unknown code. (007 P2, edge cases 7-10.)
        var punchDate = DateOnly.FromDateTime(timestamp.UtcDateTime);
        if (employee.IsSuspended || !employee.IsEmployedOn(punchDate))
            return NotPunchable(request.EmployeeCode);

        // Idempotent same-direction dedupe: a repeat within the configured window is a double-click /
        // double-swipe, not a second event — return the existing punch, create no row, publish
        // nothing. The window is a WM decision (legacy leaned on the hardware terminal); default 30s,
        // configurable. A genuine unpaired run minutes apart is NOT caught here — that is a day-level
        // exception plan 002 owns, and P2 deliberately leaves it so 002 inherits clean, de-noised data.
        var window = dedupeOptions.Value.Window;
        if (window > TimeSpan.Zero)
        {
            var earliest = timestamp - window;
            var duplicate = await db.Punches.AsNoTracking()
                .Where(p => p.EmployeeId == employee.Id
                            && p.Direction == request.Direction
                            && p.Timestamp <= timestamp
                            && p.Timestamp >= earliest)
                .OrderByDescending(p => p.Timestamp)
                .FirstOrDefaultAsync(ct);
            if (duplicate is not null)
                return new PunchResult(duplicate, null);
        }

        var punch = new Punch
        {
            EmployeeId = employee.Id,
            EmployeeCode = employee.Code,
            Timestamp = timestamp,
            Direction = request.Direction,
            Source = request.Source,
            DeviceId = request.DeviceId,
            SiteId = employee.SiteId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RecordedByUserId = recordedBy,
        };

        db.Punches.Add(punch);
        await db.SaveChangesAsync(ct);

        await eventStream.PublishAsync(EventTopics.Punches, employee.Code, new PunchRecorded(
            punch.Id, employee.Id, employee.Code, employee.FullName, employee.SiteId, employee.DepartmentId,
            punch.Timestamp, punch.Direction.ToString(), punch.Source.ToString()), ct);

        return new PunchResult(punch, null);
    }

    /// <summary>Self-service punch: caller supplies only their employee id (from their token).</summary>
    public async Task<PunchResult> RecordForEmployeeAsync(
        Guid employeeId, PunchDirection direction, PunchSource source,
        double? latitude, double? longitude, Guid? recordedBy, CancellationToken ct)
    {
        var employee = await employees.FindByIdAsync(employeeId, ct);
        if (employee is null)
            return new PunchResult(null, "Your employee record could not be found.");

        return await RecordAsync(new RecordPunchRequest(
            employee.Code, direction, source, Latitude: latitude, Longitude: longitude), recordedBy, ct);
    }

    public async Task<IReadOnlyList<RecentPunchEntry>> GetRecentForEmployeeAsync(
        Guid employeeId, int take, CancellationToken ct)
    {
        var employee = await employees.FindByIdAsync(employeeId, ct);
        var name = employee?.FullName ?? "";
        return await db.Punches.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.Timestamp)
            .Take(Math.Clamp(take, 1, 200))
            .Select(p => new RecentPunchEntry(
                p.Id, p.EmployeeId, p.EmployeeCode, name,
                p.Timestamp, p.Direction, p.Source, p.DeviceId))
            .ToListAsync(ct);
    }

    public sealed record RecentPunchEntry(
        Guid Id, Guid EmployeeId, string EmployeeCode, string EmployeeName,
        DateTimeOffset Timestamp, PunchDirection Direction, PunchSource Source, string? DeviceId);

    public async Task<IReadOnlyList<RecentPunchEntry>> GetRecentAsync(int take, CancellationToken ct)
    {
        // Restrict to employees the caller may see before touching punches — otherwise
        // the feed would leak the existence and movements of out-of-scope staff.
        //
        // Employed *today* AND not suspended, which is what ListActiveAsync used to mean and is still
        // what a live feed wants. The two halves are written out because the directory answers the
        // window question only — suspension is a separate fact and a caller replaying who was on the
        // payroll must not have it applied behind its back (IEmployeeDirectory.ListEmployedOnAsync).
        //
        // 007 P2 closed the accept-then-hide gap: RecordAsync now refuses a punch dated outside the
        // employee's window (and refuses a suspended employee), so the accept decision and this
        // visibility set apply the same predicate — one at the punch's date, one at today's.
        var visible = (await employees.ListEmployedOnAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct))
            .Where(e => !e.IsSuspended)
            .ToDictionary(e => e.Id, e => e.FullName);
        if (visible.Count == 0)
            return [];

        var visibleIds = visible.Keys.ToArray();
        var punches = await db.Punches.AsNoTracking()
            .Where(p => visibleIds.Contains(p.EmployeeId))
            .OrderByDescending(p => p.Timestamp)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

        return punches
            .Select(p => new RecentPunchEntry(
                p.Id, p.EmployeeId, p.EmployeeCode,
                visible.GetValueOrDefault(p.EmployeeId, p.EmployeeCode),
                p.Timestamp, p.Direction, p.Source, p.DeviceId))
            .ToList();
    }

    /// <summary>Everyone whose latest punch today is an In (i.e. currently clocked in).</summary>
    public async Task<LivePresence> GetLivePresenceAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-18); // ignore stale forgotten punches
        var latestPunches = await db.Punches.AsNoTracking()
            .Where(p => p.Timestamp >= since)
            .GroupBy(p => p.EmployeeId)
            .Select(g => g.OrderByDescending(p => p.Timestamp).First())
            .ToListAsync(ct);

        // Who could be at work today, against whom presence is reported: employed today and not
        // administratively suspended. Composed here rather than in the directory — see GetRecentAsync.
        var active = (await employees.ListEmployedOnAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct))
            .Where(e => !e.IsSuspended)
            .ToList();
        var byId = active.ToDictionary(e => e.Id);

        var present = latestPunches
            .Where(p => p.Direction == PunchDirection.In && byId.ContainsKey(p.EmployeeId))
            .Select(p =>
            {
                var employee = byId[p.EmployeeId];
                return new LivePresenceEntry(employee.Id, employee.Code, employee.FullName,
                    employee.JobTitle, employee.SiteId, p.Timestamp);
            })
            .OrderByDescending(e => e.Since)
            .ToList();

        return new LivePresence(present.Count, active.Count, present);
    }

    public async Task<IReadOnlyList<TimesheetDay>> GetTimesheetAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = new DateTimeOffset(from, TimeOnly.MinValue, TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero);

        var punches = await db.Punches.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.Timestamp >= start && p.Timestamp < end)
            .OrderBy(p => p.Timestamp)
            .ToListAsync(ct);

        var days = new List<TimesheetDay>();
        foreach (var group in punches.GroupBy(p => DateOnly.FromDateTime(p.Timestamp.UtcDateTime)))
        {
            var intervals = new List<TimesheetInterval>();
            DateTimeOffset? openIn = null;
            foreach (var punch in group)
            {
                if (punch.Direction == PunchDirection.In)
                {
                    if (openIn.HasValue)
                        intervals.Add(new TimesheetInterval(openIn.Value, null, null)); // missing Out anomaly
                    openIn = punch.Timestamp;
                }
                else if (openIn.HasValue)
                {
                    var hours = (punch.Timestamp - openIn.Value).TotalHours;
                    intervals.Add(new TimesheetInterval(openIn.Value, punch.Timestamp, Math.Round(hours, 2)));
                    openIn = null;
                }
            }
            if (openIn.HasValue)
                intervals.Add(new TimesheetInterval(openIn.Value, null, null));

            days.Add(new TimesheetDay(group.Key, intervals,
                Math.Round(intervals.Where(i => i.Hours.HasValue).Sum(i => i.Hours!.Value), 2)));
        }

        return days;
    }
}
