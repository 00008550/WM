using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WM.Modules.People.Data.Migrations;
using Xunit;

namespace WM.Modules.People.Tests.Data;

/// <summary>
/// <b>Read this before trusting these tests.</b> They exercise the migration <i>as a program</i> — the
/// operations it emits and the status map its SQL is written from — and they <b>never execute a line of
/// SQL</b>. There is no Postgres harness in this repository: every test project uses the EF in-memory
/// provider, which does not run migrations at all, and on this machine neither a Postgres service nor a
/// Docker engine is available to stand one up.
///
/// <para>
/// So the following are <b>not</b> covered here and must not be reported as if they were: that the DDL
/// executes; that the two <c>UPDATE</c> statements match the rows they are meant to; that
/// <c>CAST(… AT TIME ZONE 'UTC' AS date)</c> yields the date intended; that <c>Down</c> leaves a schema
/// the previous build can actually run against. What <i>was</i> verified outside the test suite is that
/// both directions generate valid provider SQL offline —
/// <c>dotnet ef migrations script</c> in each direction, pasted into the portion's PR.
/// </para>
///
/// <para>
/// This is the same gap that swallowed 001 P2's promised "migration up/down on a seeded DB" test, which
/// was never written and whose review passed anyway (<c>docs/plans/STATE.md</c>, and the audit note in
/// <c>PHASE-AUDIT.md</c>). It is named rather than papered over: a harness is real work and belongs to a
/// portion of its own, and the alternative — a test that silently skips when no database answers — is
/// how a suite comes to report green while proving nothing.
/// </para>
/// </summary>
public sealed class EmploymentMigrationTests
{
    [Fact]
    public void Every_pre_migration_status_maps_to_an_employment_window_and_back()
    {
        // The round-trip the plan requires. Forward is the status map the Up SQL is written from;
        // backward is StatusFrom, which the Down SQL is written from. Every one of the three values
        // the deleted enum could hold must survive the pair unchanged — otherwise a roll-forward and
        // roll-back would quietly relabel people.
        Assert.Equal(3, EmploymentWindowAndLeaverRecord.StatusMap.Count);

        foreach (var mapping in EmploymentWindowAndLeaverRecord.StatusMap)
        {
            var back = EmploymentWindowAndLeaverRecord.StatusFrom(mapping.Suspended, mapping.WindowEnds);
            Assert.Equal(mapping.Status, back);
        }

        // And the three are distinct windows, not three names for one — a map that sent everything to
        // (not suspended, open) would pass the loop above only if StatusFrom were equally broken, so
        // the images are pinned by value too.
        Assert.Equal([(false, false), (true, false), (false, true)],
            EmploymentWindowAndLeaverRecord.StatusMap.Select(m => (m.Suspended, m.WindowEnds)).ToArray());
    }

    [Fact]
    public void The_status_map_is_what_the_backfill_SQL_says()
    {
        // The map and the SQL are two statements of one rule (the SQL keeps integer literals so that a
        // later enum rename can never reach back and rewrite historical data). This is the test that
        // stops them drifting: every case in the map must appear in the emitted UPDATE.
        var up = Operations(m => m.UpOperations);
        var backfill = Assert.Single(up.OfType<SqlOperation>()).Sql;

        foreach (var mapping in EmploymentWindowAndLeaverRecord.StatusMap)
            Assert.Contains($"WHEN {mapping.Status} THEN {(mapping.Suspended ? "true" : "false")}", backfill);

        // The one status that ends the window, and the best-effort source of its date.
        var terminated = EmploymentWindowAndLeaverRecord.StatusMap.Single(m => m.WindowEnds);
        Assert.Contains($"\"Status\" = {terminated.Status}", backfill);
        Assert.Contains("COALESCE(\"UpdatedAt\", \"CreatedAt\")", backfill);
        // Deterministic regardless of the session's TimeZone. A bare cast is the defect this avoids.
        Assert.Contains("AT TIME ZONE 'UTC'", backfill);
        // Unrecognised values fail closed, as 001 P2's data migration does.
        Assert.Contains("ELSE true", backfill);
    }

    [Fact]
    public void The_backfill_runs_while_Status_still_exists()
    {
        // Ordering, and the actual bug in the scaffolded version of this migration: `ef migrations add`
        // emitted DropColumn("Status") FIRST, which would have read nothing and backfilled every row in
        // the estate as "Active, open-ended" — silently un-terminating every leaver.
        var up = Operations(m => m.UpOperations);

        var backfill = up.FindIndex(o => o is SqlOperation);
        var dropStatus = up.FindIndex(o => o is DropColumnOperation { Name: "Status" });

        Assert.True(backfill >= 0, "the migration must carry a data migration");
        Assert.True(dropStatus >= 0, "the migration must drop the stored status");
        Assert.True(backfill < dropStatus,
            $"the backfill must read Status before it is dropped (backfill at {backfill}, drop at {dropStatus})");

        // Same argument on the other side: the columns it writes into must exist by then.
        foreach (var column in new[] { "IsSuspended", "EmployedUntil" })
        {
            var added = up.FindIndex(o => o is AddColumnOperation { Name: var n } && n == column);
            Assert.True(added >= 0 && added < backfill, $"{column} must be added before the backfill writes it");
        }
    }

    [Fact]
    public void Down_restores_the_status_before_dropping_what_it_is_computed_from()
    {
        var down = Operations(m => m.DownOperations);

        var addStatus = down.FindIndex(o => o is AddColumnOperation { Name: "Status" });
        var restore = down.FindIndex(o => o is SqlOperation);
        var dropSuspended = down.FindIndex(o => o is DropColumnOperation { Name: "IsSuspended" });
        var dropUntil = down.FindIndex(o => o is DropColumnOperation { Name: "EmployedUntil" });

        Assert.True(addStatus >= 0 && restore >= 0 && dropSuspended >= 0 && dropUntil >= 0);
        Assert.True(addStatus < restore, "Status must exist before the restore writes it");
        Assert.True(restore < dropSuspended && restore < dropUntil,
            "the restore must read the window before the window is dropped");

        // The reverse map, in the SQL: the leaving date outranks suspension, and everything else is 0.
        // First of two — the second drops the column default that AddColumn had to invent.
        var sql = down.OfType<SqlOperation>().First().Sql;
        Assert.Contains("DROP DEFAULT", down.OfType<SqlOperation>().Last().Sql);
        Assert.Contains($"WHEN \"EmployedUntil\" IS NOT NULL THEN {EmploymentWindowAndLeaverRecord.StatusFrom(false, true)}", sql);
        Assert.Contains($"WHEN \"IsSuspended\" THEN {EmploymentWindowAndLeaverRecord.StatusFrom(true, false)}", sql);
        Assert.Contains($"ELSE {EmploymentWindowAndLeaverRecord.StatusFrom(false, false)}", sql);
    }

    [Fact]
    public void Down_undoes_every_structural_change_Up_makes()
    {
        // The classic migration defect is a Down that forgets one column, which only shows up when
        // somebody rolls back a release at 2am. Computed rather than listed, so a column added to Up
        // later fails here until Down learns to drop it.
        var up = Operations(m => m.UpOperations);
        var down = Operations(m => m.DownOperations);

        var added = up.OfType<AddColumnOperation>().Select(o => o.Name).Order().ToArray();
        var dropped = down.OfType<DropColumnOperation>().Select(o => o.Name).Order().ToArray();
        Assert.Equal(added, dropped);

        var created = up.OfType<CreateTableOperation>().Select(o => o.Name).Order().ToArray();
        var destroyed = down.OfType<DropTableOperation>().Select(o => o.Name).Order().ToArray();
        Assert.Equal(created, destroyed);

        // The rename is inverted rather than repeated: HireDate -> EmployedFrom, then back.
        var forward = Assert.Single(up.OfType<RenameColumnOperation>());
        var backward = Assert.Single(down.OfType<RenameColumnOperation>());
        Assert.Equal(("HireDate", "EmployedFrom"), (forward.Name, forward.NewName));
        Assert.Equal((forward.NewName, forward.Name), (backward.Name, backward.NewName));

        // Down adds exactly the column Up drops, and nothing else.
        Assert.Equal(
            up.OfType<DropColumnOperation>().Select(o => o.Name).Order().ToArray(),
            down.OfType<AddColumnOperation>().Select(o => o.Name).Order().ToArray());
    }

    [Fact]
    public void The_leaver_lookup_refuses_to_be_deleted_out_from_under_a_leaver()
    {
        // IsActive is only meaningful if a delete cannot take its place. RESTRICT is what makes
        // "retire the reason" the sole way to withdraw one, at the database rather than by convention.
        var foreignKey = Assert.Single(Operations(m => m.UpOperations).OfType<AddForeignKeyOperation>());

        Assert.Equal("LeavingReasons", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
    }

    /// <summary>
    /// One direction of the migration, as the list of operations EF would hand to the provider —
    /// <see cref="Migration.UpOperations"/> is how the migrator itself reads a migration. This is the
    /// whole extent of what can be exercised without a database: the operations are inspected, never
    /// executed, and never even turned into SQL here.
    /// </summary>
    private static List<MigrationOperation> Operations(
        Func<Migration, IReadOnlyList<MigrationOperation>> direction) =>
        direction(new EmploymentWindowAndLeaverRecord
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL",
        }).ToList();
}
