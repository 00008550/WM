using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WM.Modules.People.Data;
using WM.Modules.People.Data.Migrations;
using WM.Modules.People.Domain;
using Xunit;

namespace WM.Modules.People.Tests.Data;

/// <summary>
/// 007 P4's constraint, inspected rather than executed — the same limit as
/// <see cref="EmployeeCodeMigrationTests"/>. That Postgres accepts the DDL, that the repair nulls real
/// orphans and reports the count, and that the key refuses a dangling insert was checked by hand against
/// a throwaway Postgres when P4 was built; nothing in this project re-runs it.
/// </summary>
public sealed class EmployeeDepartmentMigrationTests
{
    [Fact]
    public void The_model_has_a_restricting_foreign_key_and_an_index_on_DepartmentId()
    {
        using var db = new PeopleDbContext(new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=unused").Options);
        var employee = db.Model.FindEntityType(typeof(Employee))!;

        var fk = Assert.Single(employee.GetForeignKeys(),
            k => k.Properties.Select(p => p.Name).SequenceEqual(["DepartmentId"]));
        Assert.Equal(typeof(Department), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        Assert.Equal(EmployeeDepartmentReference.ForeignKeyName, fk.GetConstraintName());
        Assert.Contains(employee.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["DepartmentId"]));
    }

    [Fact]
    public void Up_repairs_orphans_before_adding_the_key()
    {
        var up = Operations(m => m.UpOperations);

        var repair = up.FindIndex(o => o is SqlOperation { Sql: EmployeeDepartmentReference.RepairOrphansSql });
        var fk = up.FindIndex(o => o is AddForeignKeyOperation
        {
            Name: EmployeeDepartmentReference.ForeignKeyName, PrincipalTable: "Departments",
            OnDelete: ReferentialAction.Restrict,
        });

        Assert.True(repair >= 0 && fk >= 0);
        Assert.True(repair < fk, "the key cannot be created over a dangling reference");
        Assert.Contains(up, o => o is CreateIndexOperation { Name: "IX_Employees_DepartmentId" });
    }

    [Fact]
    public void The_repair_nulls_orphans_and_reports_how_many_rather_than_failing()
    {
        var sql = EmployeeDepartmentReference.RepairOrphansSql;

        Assert.Contains("SET \"DepartmentId\" = NULL", sql);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM people.\"Departments\"", sql);
        Assert.Contains("GET DIAGNOSTICS orphaned = ROW_COUNT", sql);
        Assert.Contains("RAISE WARNING", sql);
        Assert.DoesNotContain("RAISE EXCEPTION", sql);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Down_drops_the_key_and_the_index()
    {
        var down = Operations(m => m.DownOperations);

        Assert.Contains(down, o => o is DropForeignKeyOperation { Name: EmployeeDepartmentReference.ForeignKeyName });
        Assert.Contains(down, o => o is DropIndexOperation { Name: "IX_Employees_DepartmentId" });
    }

    private static List<MigrationOperation> Operations(
        Func<Migration, IReadOnlyList<MigrationOperation>> direction) =>
        direction(new EmployeeDepartmentReference
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL",
        }).ToList();
}
