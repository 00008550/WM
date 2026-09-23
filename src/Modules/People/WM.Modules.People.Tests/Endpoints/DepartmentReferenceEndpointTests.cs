using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WM.Modules.People.Data;
using WM.Modules.People.Data.Migrations;
using WM.Modules.People.Domain;
using WM.Modules.People.Services;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// 007 P4 — <c>DepartmentId</c> is a real reference; edge cases 15–18 of plan 007.
///
/// <para>
/// The in-memory provider enforces no foreign key, so everything here is the <b>application's</b>
/// half of the rule. The constraint itself is pinned by <c>EmployeeDepartmentMigrationTests</c> and
/// was exercised against Postgres when P4 was built (recorded in the portion's PR). The 23503
/// handler is reached through a labelled stand-in interceptor, as 007 P3 does for 23505.
/// </para>
/// </summary>
public sealed class DepartmentReferenceEndpointTests
{
    private static readonly Guid North = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid South = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid NorthAssembly = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SouthAssembly = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Nowhere = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Unassigned = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly DateOnly Hired = new(2024, 1, 15);
    private const string NewCode = "E3001";

    [Fact]
    public async Task Edge15_a_create_naming_a_department_that_does_not_exist_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New(North, Nowhere));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("department does not exist", await response.Content.ReadAsStringAsync());
        Assert.Empty(host.Read(db => db.Employees.AsNoTracking().Where(e => e.Code == NewCode).ToList()));
    }

    [Fact]
    public async Task Edge15_an_edit_naming_a_department_that_does_not_exist_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await host.PutEmployeeAsync(client, Ada, Edit(North, Nowhere));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NorthAssembly, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada)).DepartmentId);
    }

    [Fact]
    public async Task Edge16_a_create_with_another_sites_department_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New(North, SouthAssembly));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("different site", await response.Content.ReadAsStringAsync());
        Assert.Empty(host.Read(db => db.Employees.AsNoTracking().Where(e => e.Code == NewCode).ToList()));
    }

    [Fact]
    public async Task Edge16_an_edit_onto_another_sites_department_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await host.PutEmployeeAsync(client, Ada, Edit(North, SouthAssembly));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NorthAssembly, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada)).DepartmentId);
    }

    [Fact]
    public async Task A_department_at_the_employees_own_site_is_accepted()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var created = await client.PostAsJsonAsync("/api/employees", New(South, SouthAssembly));
        var moved = await host.PutEmployeeAsync(client, Ada, Edit(South, SouthAssembly));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var ada = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal((South, (Guid?)SouthAssembly), (ada.SiteId, ada.DepartmentId));
    }

    [Fact]
    public async Task Edge17_no_department_is_allowed()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New(North, departmentId: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(host.Read(db => db.Employees.AsNoTracking().Single(e => e.Code == NewCode)).DepartmentId);
    }

    [Fact]
    public async Task Edge17_no_department_never_satisfies_a_department_scope()
    {
        // Regression pin on EmployeeScopeExtensions.WithinScope's Departments arm — asserted, not
        // changed. A scope granting EVERY department still does not reach a person with none: null
        // is not a wildcard.
        await using var host = await PeopleEndpointHost.StartAsync(
            Departments(NorthAssembly, SouthAssembly), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        var detail = await client.GetAsync($"/api/employees/{Unassigned}");
        var visible = host.Read(db => db.Employees.AsNoTracking()
            .WithinScope(Departments(NorthAssembly, SouthAssembly)).Select(e => e.Id).ToList());

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.DoesNotContain(Unassigned, visible);
        Assert.Contains(Ada, visible); // the scope does work — it is the null that fails it
    }

    [Fact]
    public async Task Edge18_moving_site_while_keeping_the_old_sites_department_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        // Ada is North / North-Assembly. The SPA's full-replace PUT carries her department back
        // unchanged while the site picker moves her South.
        var response = await host.PutEmployeeAsync(client, Ada, Edit(South, NorthAssembly));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("different site", await response.Content.ReadAsStringAsync());
        Assert.Equal(North, host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada)).SiteId);
    }

    [Fact]
    public async Task A_department_deleted_after_the_check_is_named_in_the_400_not_called_a_leaving_reason()
    {
        // The race the 23503 handler exists for, with the stand-in raising what Postgres would name.
        await using var host = await PeopleEndpointHost.StartAsync(
            EffectiveDataScope.All(), Seed,
            configureDb: o => o.AddInterceptors(new ForeignKeyViolation(EmployeeDepartmentReference.ForeignKeyName)));
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", New(North, NorthAssembly));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("department does not exist", body);
        Assert.DoesNotContain("leaving reason", body);
    }

    [Theory]
    [InlineData("FK_Employees_Departments_DepartmentId", "department")]
    [InlineData("FK_Employees_LeavingReasons_LeavingReasonId", "leaving reason")]
    [InlineData("FK_Employees_LeaveNoticePeriods_LeaveNoticePeriodId", "notice period")]
    public void The_23503_message_names_the_reference_its_constraint_names(string constraint, string names)
    {
        Assert.Contains(names, PeopleModule.MissingReferenceMessage(constraint));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("FK_Something_Else")]
    public void An_unrecognised_23503_names_no_particular_reference(string? constraint)
    {
        var message = PeopleModule.MissingReferenceMessage(constraint);
        Assert.DoesNotContain("leaving reason", message);
        Assert.DoesNotContain("department", message);
    }

    private static EffectiveDataScope Departments(params Guid[] departmentIds) =>
        new(DataScopeKind.Departments, new HashSet<Guid>(), new HashSet<Guid>(departmentIds), null);

    private static EmployeeUpsertRequest New(Guid siteId, Guid? departmentId) =>
        new(NewCode, "Grace", "Hopper", null, null, null, siteId, departmentId, Hired);

    private static EmployeeUpsertRequest Edit(Guid siteId, Guid? departmentId) =>
        new("E1001", "Ada", "Lovelace", null, null, null, siteId, departmentId, Hired);

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = North, Name = "North" });
        db.Sites.Add(new Site { Id = South, Name = "South" });
        db.Departments.Add(new Department { Id = NorthAssembly, Name = "Assembly", SiteId = North });
        db.Departments.Add(new Department { Id = SouthAssembly, Name = "Assembly", SiteId = South });
        db.Employees.Add(new Employee
        {
            Id = Ada, Code = "E1001", FirstName = "Ada", LastName = "Lovelace",
            SiteId = North, DepartmentId = NorthAssembly, EmployedFrom = Hired,
        });
        db.Employees.Add(new Employee
        {
            Id = Unassigned, Code = "E1002", FirstName = "Bob", LastName = "Nobody",
            SiteId = North, DepartmentId = null, EmployedFrom = Hired,
        });
    }

    /// <summary>
    /// A <b>stand-in</b> for a foreign key refusing the write — the in-memory provider has none.
    /// Refuses every employee insert once the seed is in, raising the 23503 Npgsql would, with the
    /// violated constraint's name where Postgres puts it.
    /// </summary>
    private sealed class ForeignKeyViolation(string constraintName) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var inserting = eventData.Context!.ChangeTracker.Entries<Employee>()
                .Any(e => e.State == EntityState.Added && e.Entity.Code == NewCode);
            if (!inserting)
                return ValueTask.FromResult(result);
            throw new DbUpdateException("stand-in foreign key violation", new Npgsql.PostgresException(
                "insert or update on table \"Employees\" violates foreign key constraint",
                "ERROR", "ERROR", "23503", constraintName: constraintName));
        }
    }
}
