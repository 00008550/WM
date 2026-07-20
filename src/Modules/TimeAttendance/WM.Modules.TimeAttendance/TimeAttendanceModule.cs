using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WM.Modules.People.Contracts;
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
        services.AddScoped<PunchService>();
        services.AddScoped<PunchSeeder>();
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
            Guid employeeId, DateOnly? from, DateOnly? to, PunchService service, CancellationToken ct) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
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

        me.MapGet("/timesheet", async (ICurrentUser user, DateOnly? from, DateOnly? to, PunchService service, CancellationToken ct) =>
        {
            if (user.EmployeeId is not { } employeeId) return NotLinked();
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
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
    DateTimeOffset? Timestamp = null,
    string? DeviceId = null,
    double? Latitude = null,
    double? Longitude = null);
