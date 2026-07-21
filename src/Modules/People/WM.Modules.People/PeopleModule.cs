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
            if (await db.Employees.AnyAsync(e => e.Code == request.Code, ct))
                return Results.Problem($"Employee code '{request.Code}' already exists.", statusCode: StatusCodes.Status409Conflict);

            var employee = new Employee
            {
                Code = request.Code,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Phone = request.Phone,
                JobTitle = request.JobTitle,
                SiteId = request.SiteId,
                DepartmentId = request.DepartmentId,
                HireDate = request.HireDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/employees/{employee.Id}", employee);
        }).RequireAuthorization(WmPermissions.EmployeesManage);

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
    DateOnly? HireDate);

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
