using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WM.Modules.People.Data;
using WM.Modules.People.Data.Migrations;
using WM.Modules.People.Domain;
using Xunit;

namespace WM.Modules.People.Tests.Data;

/// <summary>
/// 007 P3's constraint, inspected rather than executed — the same limit, for the same reason, as
/// <see cref="EmploymentMigrationTests"/> (read its remarks). Nothing here proves Postgres accepts the
/// DDL, that the guard's <c>RAISE</c> fires on real rows, or that the index refuses a concurrent
/// duplicate. What it does pin is that the migration says the right thing, in the right order.
/// </summary>
public sealed class EmployeeCodeMigrationTests
{
    [Fact]
    public void The_unique_constraint_is_on_the_lowered_code()
    {
        var sql = EmployeeCodeCaseInsensitive.CreateIndexSql;

        Assert.StartsWith("CREATE UNIQUE INDEX", sql);
        Assert.Contains($"\"{EmployeeCodeCaseInsensitive.IndexName}\"", sql);
        Assert.Contains("people.\"Employees\" (lower(\"Code\"))", sql);
    }

    [Fact]
    public void The_constraint_and_the_application_normalise_the_same_way()
    {
        // lower() in the index; ToLower() in EmployeeCode.Matches, which Npgsql translates to lower().
        // Trim is applied to the caller's input only — stored codes are trimmed on write.
        Assert.Equal("e1030", EmployeeCode.Normalise(" E1030 "));
        Assert.Equal("0042", EmployeeCode.Normalise("0042")); // zeros are not folded (edge case 14)

        var match = EmployeeCode.Matches("E1030").Compile();
        Assert.True(match(new Employee { Code = "e1030", FirstName = "a", LastName = "b" }));
        Assert.False(match(new Employee { Code = "E10300", FirstName = "a", LastName = "b" }));
    }

    [Fact]
    public void Up_guards_then_creates_the_new_index_then_drops_the_old_one()
    {
        var up = Operations(m => m.UpOperations);
        var sql = up.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        var guard = up.FindIndex(o => o is SqlOperation { Sql: EmployeeCodeCaseInsensitive.CollisionGuardSql });
        var create = up.FindIndex(o => o is SqlOperation { Sql: EmployeeCodeCaseInsensitive.CreateIndexSql });
        var drop = up.FindIndex(o => o is DropIndexOperation { Name: "IX_Employees_Code" });

        Assert.True(guard >= 0 && create >= 0 && drop >= 0);
        Assert.True(guard < create, "the collision guard must run before the index it protects");
        Assert.True(create < drop, "the table must never be without a uniqueness constraint");
    }

    [Fact]
    public void The_guard_fails_loudly_on_case_colliding_rows_rather_than_resolving_them()
    {
        var guard = EmployeeCodeCaseInsensitive.CollisionGuardSql;

        Assert.Contains("GROUP BY lower(\"Code\")", guard);
        Assert.Contains("HAVING count(*) > 1", guard);
        Assert.Contains("RAISE EXCEPTION", guard);
        // It names the rows and changes none of them.
        Assert.Contains("string_agg(\"Code\"", guard);
        Assert.DoesNotContain("DELETE", guard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", guard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Down_restores_the_case_sensitive_index_and_drops_the_new_one()
    {
        var down = Operations(m => m.DownOperations);

        var restore = Assert.Single(down.OfType<CreateIndexOperation>());
        Assert.Equal("IX_Employees_Code", restore.Name);
        Assert.True(restore.IsUnique);
        Assert.Equal(["Code"], restore.Columns);
        Assert.Contains(down, o => o is SqlOperation { Sql: EmployeeCodeCaseInsensitive.DropIndexSql });
    }

    [Fact]
    public void The_model_no_longer_declares_a_case_sensitive_unique_index_on_Code()
    {
        // If someone "restores" HasIndex(x => x.Code).IsUnique(), the next scaffold would recreate the
        // case-sensitive rule beside the real one — and the model would claim the wrong thing.
        using var db = new PeopleDbContext(new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=unused").Options);
        var employee = db.Model.FindEntityType(typeof(Employee))!;

        Assert.DoesNotContain(employee.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["Code"]));
    }

    private static List<MigrationOperation> Operations(
        Func<Migration, IReadOnlyList<MigrationOperation>> direction) =>
        direction(new EmployeeCodeCaseInsensitive
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL",
        }).ToList();
}
