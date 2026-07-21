using Microsoft.EntityFrameworkCore;
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
    IEventStreamProducer eventStream)
{
    public async Task<PunchResult> RecordAsync(RecordPunchRequest request, Guid? recordedBy, CancellationToken ct)
    {
        var employee = await employees.FindByCodeAsync(request.EmployeeCode, ct);
        if (employee is null)
            return new PunchResult(null, $"Unknown employee code '{request.EmployeeCode}'.");

        var timestamp = request.Timestamp ?? DateTimeOffset.UtcNow;
        if (timestamp > DateTimeOffset.UtcNow.AddMinutes(5))
            return new PunchResult(null, "Punch timestamp cannot be in the future.");

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
            punch.Id, employee.Id, employee.Code, employee.FullName, employee.SiteId,
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
        var visible = (await employees.ListActiveAsync(ct)).ToDictionary(e => e.Id, e => e.FullName);
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

        var active = await employees.ListActiveAsync(ct);
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
