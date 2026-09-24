using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WM.Modules.People.Data;
using WM.Modules.People.Data.Migrations;
using WM.Modules.People.Domain;
using Xunit;

namespace WM.Modules.People.Tests.Data;

/// <summary>
/// 008 P2's migration, inspected rather than executed: there is no Postgres harness here. That
/// Postgres runs it Up, Down and Up again over seeded rows was checked by hand against a throwaway
/// <c>postgres:17-alpine</c> when P2 was built; nothing in this project re-runs it.
/// </summary>
public sealed class SiteTimeZoneMigrationTests
{
    [Fact]
    public void The_model_holds_the_zone_as_nullable()
    {
        using var db = new PeopleDbContext(new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=unused").Options);

        Assert.True(db.Model.FindEntityType(typeof(Site))!.FindProperty(nameof(Site.TimeZone))!.IsNullable);
    }

    [Fact]
    public void Up_makes_the_column_nullable_before_it_nulls_the_old_default()
    {
        var up = Operations(m => m.UpOperations);

        var alter = up.FindIndex(o => o is AlterColumnOperation { Name: "TimeZone", IsNullable: true });
        var normalise = up.FindIndex(o => o is SqlOperation { Sql: SiteTimeZoneInherits.NormaliseSql });
        Assert.True(alter >= 0 && normalise > alter);
    }

    [Fact]
    public void The_normalisation_nulls_UTC_and_unknown_zones_and_reports_rather_than_fails()
    {
        var sql = SiteTimeZoneInherits.NormaliseSql;

        Assert.Contains("WHERE \"TimeZone\" = 'UTC'", sql);
        Assert.Contains("pg_timezone_names", sql);
        Assert.Contains("RAISE NOTICE", sql);
        Assert.DoesNotContain("RAISE EXCEPTION", sql);
    }

    [Fact]
    public void Down_restores_UTC_before_the_column_becomes_required_again()
    {
        var down = Operations(m => m.DownOperations);

        var restore = down.FindIndex(o => o is SqlOperation s && s.Sql.Contains("SET \"TimeZone\" = 'UTC' WHERE \"TimeZone\" IS NULL"));
        var alter = down.FindIndex(o => o is AlterColumnOperation { Name: "TimeZone", IsNullable: false });
        Assert.True(restore >= 0 && alter > restore);
    }

    private static List<MigrationOperation> Operations(Func<Migration, IReadOnlyList<MigrationOperation>> pick) =>
        [.. pick(new SiteTimeZoneInherits())];
}
