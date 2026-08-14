using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WM.Modules.People.Contracts;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.Modules.People.Services;
using WM.SharedKernel.Common;
using WM.SharedKernel.Modules;
using WM.SharedKernel.Security;

namespace WM.Modules.People;

public sealed class PeopleModule : IModule
{
    public string Name => "People";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PeopleDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("Default"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "people")));
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<ISiteHierarchy, Services.SiteHierarchy>();
        services.AddScoped<PeopleSeeder>();
    }

    /// <summary>
    /// "Today" for an employment question asked without a date. UTC, as every other date in WM is
    /// today — <c>Site.TimeZone</c> exists and nothing resolves against it yet, so an employee whose
    /// last day is today is a leaver up to 12 hours early or late depending on the site. Recorded
    /// rather than fixed here: it is the same decision for punches, timesheets and payroll periods,
    /// and it belongs to whichever plan makes WM timezone-aware, not to one endpoint.
    /// </summary>
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var employees = endpoints.MapGroup("/api/employees").WithTags("Employees");

        // employedOn defaults to today and drives only the derived status column: the list still
        // shows everyone in scope, including leavers and pre-boarded starters. Asking as at another
        // date answers "who was a leaver in March?" without a second endpoint.
        employees.MapGet("/", async (PeopleDbContext db, IDataScopeResolver scopes, string? search, Guid? siteId, DateOnly? employedOn, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var asAt = employedOn ?? Today();

            var scope = await scopes.GetScopeAsync(ct);
            var query = db.Employees.AsNoTracking().WithinScope(scope);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{search.Trim()}%";
                query = query.Where(e =>
                    EF.Functions.ILike(e.FirstName, pattern) ||
                    EF.Functions.ILike(e.LastName, pattern) ||
                    EF.Functions.ILike(e.Code, pattern));
            }
            if (siteId.HasValue)
                query = query.Where(e => e.SiteId == siteId);

            var total = await query.CountAsync(ct);
            // The page is materialised first and the status computed afterwards, in the domain, so
            // the rule exists once. Projecting a CASE expression here would be a second copy of it
            // — which is how legacy ended up with six that disagree.
            var rows = await query
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            var items = rows.Select(e => new
            {
                e.Id, e.Code, e.FirstName, e.LastName, e.Email, e.JobTitle,
                e.SiteId, e.DepartmentId,
                e.EmployedFrom, e.EmployedUntil, e.IsSuspended,
                e.LeavingReasonId, e.LeaverComments,
                Status = e.StatusOn(asAt),
                IsEmployed = e.IsEmployedOn(asAt),
                AsAt = asAt,
            }).ToList();

            return Results.Ok(new PagedResult<object>(items, total, page, pageSize));
        }).RequireAuthorization(WmPermissions.EmployeesView);

        // Out-of-scope employees return 404, not 403: confirming a record exists but
        // is hidden would leak that the person works here.
        employees.MapGet("/{id:guid}", async (Guid id, PeopleDbContext db, IDataScopeResolver scopes, CancellationToken ct) =>
        {
            var scope = await scopes.GetScopeAsync(ct);
            var employee = await db.Employees.AsNoTracking().WithinScope(scope)
                .FirstOrDefaultAsync(e => e.Id == id, ct);
            return employee is null ? Results.NotFound() : Results.Ok(employee);
        }).RequireAuthorization(WmPermissions.EmployeesView);

        employees.MapPost("/", async (
            EmployeeUpsertRequest request, PeopleDbContext db, IDataScopeResolver scopes, CancellationToken ct) =>
        {
            if (Validate(request) is { } invalid)
                return Results.Problem(invalid, statusCode: StatusCodes.Status400BadRequest);

            // A create is scoped like an edit: you may not file someone where you would not then be
            // able to see them (003 decision 3 — a create you cannot see is indistinguishable from
            // one that failed). Checked ahead of the code and site probes below so a caller outside
            // the scope cannot use a 409 to learn which badge numbers are taken.
            var scope = await scopes.GetScopeAsync(ct);
            if (!scope.PermitsWrite(employeeId: null, request.SiteId, request.DepartmentId))
                return OutOfScope();

            var code = request.Code.Trim();
            // Case-insensitive: 'E1030' and 'e1030' are the same badge number.
            if (await db.Employees.AnyAsync(e => e.Code.ToLower() == code.ToLower(), ct))
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct))
                return Results.Problem("The selected site does not exist.", statusCode: StatusCodes.Status400BadRequest);
            if (await LeavingReasonRefused(db, request, currentReasonId: null, ct) is { } refused)
                return refused;

            var employee = new Employee
            {
                Code = code,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Email = Blank(request.Email),
                Phone = Blank(request.Phone),
                JobTitle = Blank(request.JobTitle),
                SiteId = request.SiteId,
                DepartmentId = request.DepartmentId,
                EmployedFrom = request.EmployedFrom ?? Today(),
                IsSuspended = request.IsSuspended ?? false,
            };
            ApplyLeaving(employee, request);
            db.Employees.Add(employee);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // The unique index is the real guarantee; the check above races.
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Created($"/api/employees/{employee.Id}", employee);
        }).RequireAuthorization(WmPermissions.EmployeesManage);

        employees.MapPut("/{id:guid}", async (
            Guid id, EmployeeUpsertRequest request, PeopleDbContext db, IDataScopeResolver scopes, CancellationToken ct) =>
        {
            if (Validate(request) is { } invalid)
                return Results.Problem(invalid, statusCode: StatusCodes.Status400BadRequest);

            // Edit is scoped like read: you cannot modify someone you cannot see.
            var scope = await scopes.GetScopeAsync(ct);
            var employee = await db.Employees.WithinScope(scope).FirstOrDefaultAsync(e => e.Id == id, ct);
            if (employee is null)
                return Results.NotFound();

            // And the post-image is scoped too: an edit may not move someone out of the caller's own
            // scope. Legacy allows exactly that (TLW-AUTHORIZATION-MODEL.md §5 — it checks the
            // pre-image only); 003 decision 3 refuses it. Checked before anything is assigned, so a
            // refusal leaves the tracked entity untouched.
            if (!scope.PermitsWrite(id, request.SiteId, request.DepartmentId))
                return OutOfScope();

            var code = request.Code.Trim();
            if (await db.Employees.AnyAsync(e => e.Code.ToLower() == code.ToLower() && e.Id != id, ct))
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct))
                return Results.Problem("The selected site does not exist.", statusCode: StatusCodes.Status400BadRequest);
            // The reason already on the record is always acceptable, even once retired — otherwise
            // retiring a reason would make every leaver who carries it uneditable.
            if (await LeavingReasonRefused(db, request, employee.LeavingReasonId, ct) is { } refused)
                return refused;

            employee.Code = code;
            employee.FirstName = request.FirstName.Trim();
            employee.LastName = request.LastName.Trim();
            employee.Email = Blank(request.Email);
            employee.Phone = Blank(request.Phone);
            employee.JobTitle = Blank(request.JobTitle);
            employee.SiteId = request.SiteId;
            employee.DepartmentId = request.DepartmentId;
            if (request.EmployedFrom is { } from) employee.EmployedFrom = from;
            if (request.IsSuspended is { } suspended) employee.IsSuspended = suspended;
            ApplyLeaving(employee, request);
            employee.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Ok(employee);
        }).RequireAuthorization(WmPermissions.EmployeesManage);

        static string? Validate(EmployeeUpsertRequest r) =>
            string.IsNullOrWhiteSpace(r.Code) ? "Employee code is required."
            : string.IsNullOrWhiteSpace(r.FirstName) ? "First name is required."
            : string.IsNullOrWhiteSpace(r.LastName) ? "Last name is required."
            : r.SiteId == Guid.Empty ? "A site must be selected."
            // The window must be a window. Nothing downstream — accruals, replay, a timesheet —
            // has a sane answer for an employment period that ends before it starts.
            : r.EmployedUntil is { } until && r.EmployedFrom is { } from && until < from
                ? "The last day of employment cannot be before the first."
            // A reason without a leaving date is the contradiction legacy avoids by writing the two
            // together (SetEmployeesLeaver:122-145). Refused rather than silently dropped, because
            // silently dropping it loses an HR answer the user thought they had recorded.
            : r.EmployedUntil is null && (r.LeavingReasonId is not null || !string.IsNullOrWhiteSpace(r.LeaverComments))
                ? "A leaving reason or leaver comments need a last day of employment."
            : null;

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // The leaver record, written as one decision: date, reason and comments travel together.
        // CLEARING THE DATE CLEARS BOTH — legacy un-leaves with
        // `set IsActive = 1, DischargeDate = null, LeaveReasonId = null`
        // (PersonnelService.SetEmployeesActive:151-173), and a re-hired employee keeping the reason
        // they left for last time is a data defect waiting to be printed on a report.
        static void ApplyLeaving(Employee employee, EmployeeUpsertRequest request)
        {
            employee.EmployedUntil = request.EmployedUntil;
            if (request.EmployedUntil is null)
            {
                employee.LeavingReasonId = null;
                employee.LeaverComments = null;
                return;
            }
            employee.LeavingReasonId = request.LeavingReasonId;
            employee.LeaverComments = Blank(request.LeaverComments);
        }

        // A leaving reason must exist, and must still be offered — unless it is the one already on
        // the record. That asymmetry is the point of IsActive: retiring a reason stops new leavers
        // being filed under it without orphaning the ones already filed.
        static async Task<IResult?> LeavingReasonRefused(
            PeopleDbContext db, EmployeeUpsertRequest request, Guid? currentReasonId, CancellationToken ct)
        {
            if (request.LeavingReasonId is not { } reasonId || reasonId == currentReasonId)
                return null;

            var reason = await db.LeavingReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reasonId, ct);
            return reason switch
            {
                null => Results.Problem(
                    "The selected leaving reason does not exist.", statusCode: StatusCodes.Status400BadRequest),
                { IsActive: false } => Results.Problem(
                    $"The leaving reason '{reason.Name}' has been retired and cannot be assigned.",
                    statusCode: StatusCodes.Status400BadRequest),
                _ => null,
            };
        }

        // 403 rather than the 404 the read paths use: the caller chose this destination, so refusing
        // it discloses nothing they did not already supply. The pre-image check above stays 404 for
        // the opposite reason — there, the record's existence is the secret.
        static IResult OutOfScope() => Results.Problem(
            "The site or department you selected is outside your data scope.",
            statusCode: StatusCodes.Status403Forbidden);

        // The maintained leaving-reason vocabulary. Ships EMPTY — resignation, redundancy, TUPE and
        // dismissal are one customer's list and not another's, so WM seeds none of them.
        //
        // Read-only in this portion: it is the surface that decides what is "offered", which is the
        // half of IsActive a leaver record depends on. Maintenance (create/rename/retire, and the
        // screen for it) is not in 007 P1's Done-when and is recorded in the plan's As-built note.
        endpoints.MapGet("/api/leaving-reasons", async (PeopleDbContext db, bool includeRetired, CancellationToken ct) =>
        {
            var query = db.LeavingReasons.AsNoTracking();
            // Retired reasons are readable — a leaver filed under one must still render — but they
            // are not offered, so they are excluded unless the caller says otherwise.
            if (!includeRetired)
                query = query.Where(r => r.IsActive);
            return Results.Ok(await query.OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.Name, r.IsActive })
                .ToListAsync(ct));
        }).RequireAuthorization(WmPermissions.EmployeesView).WithTags("Employees");

        var sites = endpoints.MapGroup("/api/sites").WithTags("Sites");

        sites.MapGet("/", async (PeopleDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Sites.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)))
            .RequireAuthorization(WmPermissions.EmployeesView);

        // Self-service: the signed-in user's OWN employee record. Id comes from the
        // token claim, never the request — an employee can only ever see themselves.
        endpoints.MapGet("/api/me/employee", async (ICurrentUser user, PeopleDbContext db, CancellationToken ct) =>
        {
            if (user.EmployeeId is not { } employeeId)
                return Results.NotFound(new { message = "This account is not linked to an employee." });
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            return employee is null ? Results.NotFound() : Results.Ok(employee);
        }).RequireAuthorization(WmPermissions.SelfService).WithTags("Self-service");
    }
}

/// <summary>
/// A full-replace body. The three employment fields are nullable so that an omitted field means
/// "leave it alone" on a <c>PUT</c> and takes a default on a <c>POST</c> — except
/// <paramref name="EmployedUntil"/>, whose <b>absence is meaningful</b>: it is how an employee is
/// un-left, and it clears the reason and comments with it.
/// </summary>
public sealed record EmployeeUpsertRequest(
    string Code,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid SiteId,
    Guid? DepartmentId,
    DateOnly? EmployedFrom,
    DateOnly? EmployedUntil = null,
    bool? IsSuspended = null,
    Guid? LeavingReasonId = null,
    string? LeaverComments = null);

internal sealed class EmployeeDirectory(PeopleDbContext db, IDataScopeResolver scopes) : IEmployeeDirectory
{
    public async Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        await Scoped(await scopes.GetScopeAsync(ct))
            .Where(e => e.Code == code)
            .Select(Summary)
            .FirstOrDefaultAsync(ct);

    public async Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await Scoped(await scopes.GetScopeAsync(ct))
            .Where(e => e.Id == id)
            .Select(Summary)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnAsync(
        DateOnly on, CancellationToken ct = default) =>
        await Scoped(await scopes.GetScopeAsync(ct))
            .Where(Employee.EmployedOn(on))
            .Select(Summary)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnUnscopedAsync(
        DateOnly on, CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Where(Employee.EmployedOn(on))
            .Select(Summary)
            .ToListAsync(ct);

    private IQueryable<Employee> Scoped(EffectiveDataScope scope) =>
        db.Employees.AsNoTracking().WithinScope(scope);

    private static readonly System.Linq.Expressions.Expression<Func<Employee, EmployeeSummary>> Summary =
        e => new EmployeeSummary(
            e.Id, e.Code, e.FirstName + " " + e.LastName, e.JobTitle, e.SiteId, e.DepartmentId,
            e.EmployedFrom, e.EmployedUntil, e.IsSuspended);
}
