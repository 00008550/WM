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

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var employees = endpoints.MapGroup("/api/employees").WithTags("Employees");

        employees.MapGet("/", async (PeopleDbContext db, IDataScopeResolver scopes, string? search, Guid? siteId, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

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
            var items = await query
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(e => new
                {
                    e.Id, e.Code, e.FirstName, e.LastName, e.Email, e.JobTitle,
                    e.SiteId, e.DepartmentId, e.Status, e.HireDate,
                })
                .ToListAsync(ct);

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

        employees.MapPost("/", async (EmployeeUpsertRequest request, PeopleDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { } invalid)
                return Results.Problem(invalid, statusCode: StatusCodes.Status400BadRequest);

            var code = request.Code.Trim();
            // Case-insensitive: 'E1030' and 'e1030' are the same badge number.
            if (await db.Employees.AnyAsync(e => e.Code.ToLower() == code.ToLower(), ct))
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct))
                return Results.Problem("The selected site does not exist.", statusCode: StatusCodes.Status400BadRequest);

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
                HireDate = request.HireDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            };
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

            var code = request.Code.Trim();
            if (await db.Employees.AnyAsync(e => e.Code.ToLower() == code.ToLower() && e.Id != id, ct))
                return Results.Problem($"Employee code '{code}' already exists.", statusCode: StatusCodes.Status409Conflict);
            if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId, ct))
                return Results.Problem("The selected site does not exist.", statusCode: StatusCodes.Status400BadRequest);

            employee.Code = code;
            employee.FirstName = request.FirstName.Trim();
            employee.LastName = request.LastName.Trim();
            employee.Email = Blank(request.Email);
            employee.Phone = Blank(request.Phone);
            employee.JobTitle = Blank(request.JobTitle);
            employee.SiteId = request.SiteId;
            employee.DepartmentId = request.DepartmentId;
            if (request.HireDate is { } hired) employee.HireDate = hired;
            if (request.Status is { } status) employee.Status = status;
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
            : null;

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

public sealed record EmployeeUpsertRequest(
    string Code,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid SiteId,
    Guid? DepartmentId,
    DateOnly? HireDate,
    EmployeeStatus? Status = null);

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

    public async Task<IReadOnlyList<EmployeeSummary>> ListActiveAsync(CancellationToken ct = default) =>
        await Scoped(await scopes.GetScopeAsync(ct))
            .Where(e => e.Status == EmployeeStatus.Active)
            .Select(Summary)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeSummary>> ListAllActiveUnscopedAsync(CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active)
            .Select(Summary)
            .ToListAsync(ct);

    private IQueryable<Employee> Scoped(EffectiveDataScope scope) =>
        db.Employees.AsNoTracking().WithinScope(scope);

    private static readonly System.Linq.Expressions.Expression<Func<Employee, EmployeeSummary>> Summary =
        e => new EmployeeSummary(e.Id, e.Code, e.FirstName + " " + e.LastName, e.JobTitle, e.SiteId);
}
