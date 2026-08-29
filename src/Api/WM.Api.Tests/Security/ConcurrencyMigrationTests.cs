using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// The 011 P5 migrations, asserted as <b>operations</b> — which is one of two halves, and the halves
/// are not interchangeable.
///
/// <para>
/// <b>What this covers:</b> that each migration adds the column it is supposed to, in the right
/// schema and table, as a non-null <c>uuid</c>, and that it still carries the backfill. <b>What it
/// does not cover:</b> that the SQL runs. There is no Postgres harness in this repository — 009 P4
/// is the standing gap — and Docker's Linux engine would not start on the machine this was built on,
/// so <b>the SQL half has not been executed against a real Postgres</b>. That is stated rather than
/// implied: 007 P1's precedent is to run it by hand and label the two halves differently, and 001 P2
/// promised a migration test that no harness existed to run. An operations test that let itself be
/// read as a SQL test would be the same failure with extra steps.
/// </para>
/// <para>
/// The one line most worth pinning is the backfill. <c>AddColumn</c> stamps every existing row with
/// the all-zero uuid, and both write paths refuse an echoed empty token — so a migration that lost
/// its <c>UPDATE ... gen_random_uuid()</c> would leave every record that predates the upgrade
/// permanently uneditable, and nothing else in the suite would notice. Regenerating either migration
/// with <c>dotnet ef</c> drops that line, which is exactly when this test needs to fail.
/// </para>
/// </summary>
public class ConcurrencyMigrationTests
{
    [Theory]
    [InlineData(typeof(WM.Modules.People.Data.Migrations.AddConcurrencyVersion), "people", "Employees")]
    [InlineData(typeof(WM.Modules.Identity.Data.Migrations.AddConcurrencyVersion), "identity", "Users")]
    [InlineData(typeof(WM.Modules.Identity.Data.Migrations.AddConcurrencyVersion), "identity", "SecurityGroups")]
    public void The_version_column_is_added_as_a_non_null_uuid(Type migration, string schema, string table)
    {
        var added = Operations(migration).OfType<AddColumnOperation>()
            .Single(op => op.Schema == schema && op.Table == table && op.Name == "Version");

        // uuid, not xmin and not a system column: the token has to be something the in-memory
        // provider can populate, or every concurrency test passes vacuously. See AuditableEntity.
        Assert.Equal("uuid", added.ColumnType);
        Assert.False(added.IsNullable);
    }

    [Theory]
    [InlineData(typeof(WM.Modules.People.Data.Migrations.AddConcurrencyVersion), "people", "Employees")]
    [InlineData(typeof(WM.Modules.Identity.Data.Migrations.AddConcurrencyVersion), "identity", "Users")]
    [InlineData(typeof(WM.Modules.Identity.Data.Migrations.AddConcurrencyVersion), "identity", "SecurityGroups")]
    public void Existing_rows_are_backfilled_with_distinct_tokens_rather_than_left_all_zero(
        Type migration, string schema, string table)
    {
        var backfill = Operations(migration).OfType<SqlOperation>()
            .SingleOrDefault(op => op.Sql.Contains($"{schema}.\"{table}\""));

        Assert.NotNull(backfill);
        // gen_random_uuid() per row, not one shared literal — two rows sharing a token would each be
        // a valid key for the other.
        Assert.Contains("gen_random_uuid()", backfill.Sql);
        Assert.Contains("\"Version\"", backfill.Sql);
    }

    [Theory]
    [InlineData(typeof(WM.Modules.People.Data.Migrations.AddConcurrencyVersion), 1)]
    [InlineData(typeof(WM.Modules.Identity.Data.Migrations.AddConcurrencyVersion), 2)]
    public void The_migration_is_reversible(Type migration, int columnCount)
    {
        // Down drops exactly what Up added. §13A's rollback promise is its own plan (011 D3), but a
        // migration that cannot be reversed makes that plan's job harder for no reason.
        var instance = (Migration)Activator.CreateInstance(migration)!;

        Assert.Equal(columnCount, instance.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.Equal(columnCount, instance.DownOperations.OfType<DropColumnOperation>().Count());
    }

    private static IReadOnlyList<MigrationOperation> Operations(Type migration) =>
        ((Migration)Activator.CreateInstance(migration)!).UpOperations;
}
