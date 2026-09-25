using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Modules;
using WM.SharedKernel.Security;

namespace WM.Modules.TimeAttendance;

public sealed class TimeAttendanceModule : IModule
{
    public string Name => "TimeAttendance";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TimeAttendanceDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Default"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "time_attendance")));

        // The same-direction dedupe window. Bound and fault-checked at composition, exactly as
        // Identity does AccountLockoutOptions — a misconfiguration should stop the host booting, not
        // surface as silently-duplicated punches in production.
        services.AddOptions<PunchDeduplicationOptions>()
            .Bind(configuration.GetSection(PunchDeduplicationOptions.SectionName));

        var dedupe = configuration.GetSection(PunchDeduplicationOptions.SectionName).Get<PunchDeduplicationOptions>()
                     ?? new PunchDeduplicationOptions();
        if (PunchDeduplicationOptions.DescribeFault(dedupe) is { } dedupeFault)
            throw new InvalidOperationException(dedupeFault);

        // 008 P5: when a client's punch clock earns a flag. Same composition-time check.
        services.AddOptions<PunchTimingOptions>()
            .Bind(configuration.GetSection(PunchTimingOptions.SectionName));
        var timing = configuration.GetSection(PunchTimingOptions.SectionName).Get<PunchTimingOptions>()
                     ?? new PunchTimingOptions();
        if (PunchTimingOptions.DescribeFault(timing) is { } timingFault)
            throw new InvalidOperationException(timingFault);

        services.AddScoped<PunchService>();
        services.AddScoped<PunchSeeder>();
        // 008 P4: the owning-day seam — since 010 P2, legacy's swipe→day allocation — and the one-off
        // fill of punches recorded before the local day was frozen on the row. IClockingDays is the
        // Clocking store as allocation sees it; plan 002 replaces the punch-backed interim.
        services.AddScoped<IOwningDayResolver, DayAllocationService>();
        services.AddScoped<IClockingDays, PunchBackedClockingDays>();
        services.AddScoped<PunchLocalDateBackfill>();
        services.AddScoped<IDayTemplateDirectory, DayTemplateDirectory>();
    }

    /// <summary>Which clock WM trusts for a punch, for an API client to read (008 P5; see
    /// <see cref="PunchTimingOptions"/>).</summary>
    internal const string RecordPunchContract =
        "`timestamp` is the instant the punch happened, ISO 8601 WITH a UTC offset — the device's local "
        + "offset, e.g. `2026-09-25T08:00:00+05:00`. An offset is required: a timestamp without one "
        + "(`2026-09-25T08:00:00`) is refused with 400, never guessed. Omit `timestamp` to have the "
        + "server stamp the punch now. A timestamp more than 5 minutes ahead of the server's clock is "
        + "refused with 400. The punch's own instant decides its local day. "
        + "`receivedAt` is the server's clock when the punch arrived, stored beside `timestamp` and never "
        + "substituted for it (equal to it when the server stamped the punch). "
        + "A past punch is accepted — offline queues are expected — and `flags` records anomalies without "
        + "dropping the punch: `Late` (1) when `timestamp` is older than `PunchTiming:LateAfter` "
        + "(default 1 hour) on arrival; `OffsetMismatch` (2) when the timestamp's offset differs from "
        + "the employee's home-site zone at that instant by more than that zone's DST shift in force "
        + "then (none for a zone without DST — so `Z` from a device outside UTC is flagged).";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var punches = endpoints.MapGroup("/api/punches").WithTags("Punches");

        punches.MapPost("/", async (RecordPunchRequest request, PunchService service, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await service.RecordAsync(request, user.UserId, ct);
            return result.Match(
                punch => Results.Created($"/api/punches/{punch.Id}", punch),
                error => Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
        }).RequireAuthorization(WmPermissions.PunchesRecord)
          .WithSummary("Record a punch — the contract an offline client's queue relies on (008 P5).")
          .WithDescription(RecordPunchContract);

        punches.MapGet("/recent", async (PunchService service, int take = 50, CancellationToken ct = default) =>
            Results.Ok(await service.GetRecentAsync(take, ct)))
            .Produces<IReadOnlyList<PunchService.RecentPunchEntry>>()
            .RequireAuthorization(WmPermissions.AttendanceView);

        var attendance = endpoints.MapGroup("/api/attendance").WithTags("Attendance");

        attendance.MapGet("/live", async (PunchService service, CancellationToken ct) =>
            Results.Ok(await service.GetLivePresenceAsync(ct)))
            .Produces<LivePresence>()
            .RequireAuthorization(WmPermissions.AttendanceView);

        attendance.MapGet("/timesheet/{employeeId:guid}", async (
            Guid employeeId, DateOnly? from, DateOnly? to,
            PunchService service, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            // FindByIdAsync is scope-aware: an employee outside the caller's scope
            // is indistinguishable from one that does not exist.
            if (await employees.FindByIdAsync(employeeId, ct) is not { } employee)
                return Results.NotFound();

            // The range is of LOCAL days (008 P4), so its default end is the employee's local today.
            var end = to ?? await service.LocalTodayAsync(employee, ct);
            var start = from ?? end.AddDays(-6);
            return Results.Ok(await service.GetTimesheetAsync(employeeId, start, end, ct));
        }).Produces<IReadOnlyList<TimesheetDay>>()
          .RequireAuthorization(WmPermissions.AttendanceView);

        // Self-service: everything below is scoped to the caller's OWN linked employee.
        var me = endpoints.MapGroup("/api/me").WithTags("Self-service")
            .RequireAuthorization(WmPermissions.SelfService);

        me.MapGet("/punches", async (ICurrentUser user, PunchService service, int take = 20, CancellationToken ct = default) =>
            user.EmployeeId is { } employeeId
                ? Results.Ok(await service.GetRecentForEmployeeAsync(employeeId, take, ct))
                : NotLinked())
            .Produces<IReadOnlyList<PunchService.RecentPunchEntry>>();

        me.MapGet("/timesheet", async (
            ICurrentUser user, DateOnly? from, DateOnly? to,
            PunchService service, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            if (user.EmployeeId is not { } employeeId) return NotLinked();
            DateOnly end;
            if (to is { } explicitEnd)
                end = explicitEnd;
            // Their own record, unscoped: a /me read must not depend on the caller's scope over
            // other people — a site-scoped manager filed outside that site is still themselves.
            else if (await employees.FindSelfAsync(employeeId, ct) is { } employee)
                end = await service.LocalTodayAsync(employee, ct); // their local today (008 P4)
            else
                return Results.Problem("Your employee record could not be found.", statusCode: StatusCodes.Status404NotFound);
            var start = from ?? end.AddDays(-6);
            return Results.Ok(await service.GetTimesheetAsync(employeeId, start, end, ct));
        }).Produces<IReadOnlyList<TimesheetDay>>();

        me.MapPost("/punch", async (SelfPunchRequest request, ICurrentUser user, PunchService service, CancellationToken ct) =>
        {
            if (user.EmployeeId is not { } employeeId) return NotLinked();
            var result = await service.RecordForEmployeeAsync(
                employeeId, request.Direction, PunchSource.Web, request.Latitude, request.Longitude, user.UserId, ct);
            return result.Match(
                punch => Results.Created($"/api/punches/{punch.Id}", punch),
                error => Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
        });

        static IResult NotLinked() =>
            Results.Problem("This account is not linked to an employee.", statusCode: StatusCodes.Status409Conflict);
    }
}

public sealed record SelfPunchRequest(PunchDirection Direction, double? Latitude = null, double? Longitude = null);

public sealed record RecordPunchRequest(
    string EmployeeCode,
    PunchDirection Direction,
    PunchSource Source = PunchSource.Web,
    // ISO 8601 WITH an offset, or omitted for "now" (008 P5 — see PunchTimingOptions for the
    // contract). A string so an offset-less value can be refused rather than read as server-local.
    [property: Description("ISO 8601 date-time WITH a UTC offset (Z or ±hh:mm), e.g. "
        + "2026-09-25T08:00:00+05:00. Offset-less values are refused (400); more than 5 minutes ahead "
        + "is refused (400); omitted means server time. See the operation description for flags.")]
    [property: DataType(DataType.DateTime)]
    string? Timestamp = null,
    string? DeviceId = null,
    double? Latitude = null,
    double? Longitude = null);
