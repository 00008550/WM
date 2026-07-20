using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WM.Modules.People.Contracts;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
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
        services.AddScoped<PeopleSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var employees = endpoints.MapGroup("/api/employees").WithTags("Employees");

        employees.MapGet("/", async (PeopleDbContext db, string? search, Guid? siteId, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.Employees.AsNoTracking();
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

        employees.MapGet("/{id:guid}", async (Guid id, PeopleDbContext db, CancellationToken ct) =>
            await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct) is { } employee
                ? Results.Ok(employee)
                : Results.NotFound())
            .RequireAuthorization(WmPermissions.EmployeesView);

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

internal sealed class EmployeeDirectory(PeopleDbContext db) : IEmployeeDirectory
{
    public async Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Code == code)
            .Select(e => new EmployeeSummary(e.Id, e.Code, e.FirstName + " " + e.LastName, e.JobTitle, e.SiteId))
            .FirstOrDefaultAsync(ct);

    public async Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EmployeeSummary(e.Id, e.Code, e.FirstName + " " + e.LastName, e.JobTitle, e.SiteId))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EmployeeSummary>> ListActiveAsync(CancellationToken ct = default) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active)
            .Select(e => new EmployeeSummary(e.Id, e.Code, e.FirstName + " " + e.LastName, e.JobTitle, e.SiteId))
            .ToListAsync(ct);
}
