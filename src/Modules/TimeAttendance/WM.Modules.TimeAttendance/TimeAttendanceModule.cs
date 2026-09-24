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
        // 008 P4: the owning-day seam (plan 002 replaces its body) and the one-off fill of punches
        // recorded before the local day was frozen on the row.
        services.AddSingleton<IOwningDayResolver, LocalCalendarDayResolver>();
        services.AddScoped<PunchLocalDateBackfill>();
        services.AddScoped<IDayTemplateDirectory, DayTemplateDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var punches = endpoints.MapGroup("/api/punches").WithTags("Punches");

        punches.MapPost("/", async (RecordPunchRequest request, PunchService service, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await service.RecordAsync(request, user.UserId, ct);
            return result.Match(
                punch => Results.Created($"/api/punches/{punch.Id}", punch),
                error => Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
        }).RequireAuthorization(WmPermissions.PunchesRecord);

        punches.MapGet("/recent", async (PunchService service, int take = 50, CancellationToken ct = default) =>
            Results.Ok(await service.GetRecentAsync(take, ct)))
            .RequireAuthorization(WmPermissions.AttendanceView);

        var attendance = endpoints.MapGroup("/api/attendance").WithTags("Attendance");

        attendance.MapGet("/live", async (PunchService service, CancellationToken ct) =>
            Results.Ok(await service.GetLivePresenceAsync(ct)))
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
        }).RequireAuthorization(WmPermissions.AttendanceView);

        // Self-service: everything below is scoped to the caller's OWN linked employee.
        var me = endpoints.MapGroup("/api/me").WithTags("Self-service")
            .RequireAuthorization(WmPermissions.SelfService);

        me.MapGet("/punches", async (ICurrentUser user, PunchService service, int take = 20, CancellationToken ct = default) =>
            user.EmployeeId is { } employeeId
                ? Results.Ok(await service.GetRecentForEmployeeAsync(employeeId, take, ct))
                : NotLinked());

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
        });

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
    string? Timestamp = null,
    string? DeviceId = null,
    double? Latitude = null,
    double? Longitude = null);
