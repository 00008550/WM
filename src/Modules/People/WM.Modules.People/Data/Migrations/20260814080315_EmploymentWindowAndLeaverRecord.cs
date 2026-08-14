using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.People.Data.Migrations
{
    /// <summary>
    /// Employment stops being an undated enum and becomes what legacy models: two facts, one of them
    /// a date — <c>IsSuspended</c> (legacy <c>IsActive</c>, inverted) and <c>EmployedUntil</c>
    /// (legacy <c>DischargeDate</c>, last day <b>inclusive</b>) — plus the leaver record legacy keeps
    /// beside it: a reason from a maintained lookup and free-text comments.
    ///
    /// <para>
    /// <b>Naming divergence, deliberate.</b> Legacy's lookup is <c>dbo.LeaveReasons</c> and its column
    /// is <c>Employees.LeaveReasonId</c>. WM's are <c>LeavingReasons</c> and <c>LeavingReasonId</c>,
    /// by the user's decision of 2026-08-06 (plan 007 decision 4): in HR English "leave" means an
    /// <i>absence</i>, and legacy's absence vocabulary is a different 35-column rule-carrying table
    /// (<c>dbo.Absence</c>). <c>LeaveReason</c> beside a future <c>AbsenceType</c> would invite exactly
    /// that conflation. This is a rename, not a typo, and legacy keeps its own name in every citation.
    /// </para>
    ///
    /// <para>
    /// <b>The backfill of <c>EmployedUntil</c> for existing <c>Terminated</c> rows is BEST-EFFORT, and
    /// cannot be otherwise: WM never captured a leaving date.</b> <c>UpdatedAt ?? CreatedAt</c> is the
    /// closest thing on the record to "when did this become true", and it is only an approximation —
    /// any later edit to a terminated employee moved <c>UpdatedAt</c> past the day they actually left,
    /// and a row terminated in a bulk import never had an <c>UpdatedAt</c> at all. The date is
    /// therefore <i>plausible</i>, not <i>true</i>, and anything that treats it as a fact — a payroll
    /// re-run, an accrual pro-rate, a replay — will be wrong by however long that gap was. It is
    /// recorded here rather than in a backlog note because there is no later opportunity to recover
    /// the information: it was never written down.
    /// </para>
    /// </summary>
    public partial class EmploymentWindowAndLeaverRecord : Migration
    {
        /// <summary>
        /// The status → employment-window map, as one table. It is public because
        /// <c>EmploymentMigrationTests</c> asserts the SQL below against it and round-trips it through
        /// <see cref="StatusFrom"/>; nothing in the product reads it.
        ///
        /// <para>
        /// The SQL keeps integer <b>literals</b> and does not reference <c>EmployeeStatus</c>, for the
        /// same reason the 001 P2 data migration does not: a later rename or reordering of a C# enum
        /// must never be able to reach back and rewrite historical data. This table is a frozen copy
        /// of what the enum meant on 2026-08-14, and it belongs to the migration, not to the domain.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<StatusMapping> StatusMap =
        [
            //          was            suspended?  window ends?
            new(0, "Active",           false,      false),  // employed, open-ended
            new(1, "OnLeave",          true,       false),  // WM's invention: administratively out, still employed
            new(2, "Terminated",       false,      true),   // gone; the window is what ended, not their standing
        ];

        /// <param name="Status">The value stored in the dropped <c>Employees.Status</c> column.</param>
        /// <param name="Was">Its name in the enum this migration deletes, for the reader.</param>
        /// <param name="Suspended">What <c>IsSuspended</c> becomes.</param>
        /// <param name="WindowEnds">Whether <c>EmployedUntil</c> gets a date (the best-effort one).</param>
        public sealed record StatusMapping(int Status, string Was, bool Suspended, bool WindowEnds);

        /// <summary>
        /// <see cref="Down"/>'s direction, in C#, so the round-trip can be asserted without a database.
        /// Every one of the three pre-migration statuses maps to a window and back to itself.
        ///
        /// <para>
        /// It is <b>not</b> injective in the other direction, and cannot be: a row written after this
        /// migration may be suspended <i>and</i> have a leaving date — a state the old enum could not
        /// express — and rolling back keeps the leaving date's meaning (<c>Terminated</c>) over the
        /// suspension's. A future <c>EmployedFrom</c> (a pre-boarded starter) becomes
        /// <c>Active</c>, because the old enum had no way to say "not yet". The leaving reason and
        /// comments are lost outright: <see cref="Down"/> drops the table that holds them.
        /// </para>
        /// </summary>
        public static int StatusFrom(bool suspended, bool windowEnds) =>
            windowEnds ? 2 : suspended ? 1 : 0;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HireDate carried the right fact under a name that only described one end of a window.
            // A rename rather than an add+copy+drop, so no data moves and Down is exact.
            migrationBuilder.RenameColumn(
                name: "HireDate",
                schema: "people",
                table: "Employees",
                newName: "EmployedFrom");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmployedUntil",
                schema: "people",
                table: "Employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                schema: "people",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LeaverComments",
                schema: "people",
                table: "Employees",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeavingReasonId",
                schema: "people",
                table: "Employees",
                type: "uuid",
                nullable: true);

            // Ships empty. Leaving reasons are customer vocabulary — one install's "TUPE" is another
            // install's nothing — so WM seeds none and offers the lookup instead of an enum.
            migrationBuilder.CreateTable(
                name: "LeavingReasons",
                schema: "people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavingReasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_LeavingReasonId",
                schema: "people",
                table: "Employees",
                column: "LeavingReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_LeavingReasons_Name",
                schema: "people",
                table: "LeavingReasons",
                column: "Name",
                unique: true);

            // RESTRICT, not cascade or set-null: a reason that is in use cannot be deleted, only
            // retired via IsActive. That is the difference between keeping a leaver's history and
            // quietly erasing it, and the database is what makes it true.
            migrationBuilder.AddForeignKey(
                name: "FK_Employees_LeavingReasons_LeavingReasonId",
                schema: "people",
                table: "Employees",
                column: "LeavingReasonId",
                principalSchema: "people",
                principalTable: "LeavingReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- data migration: the enum becomes a window ----
            //
            // Runs BEFORE Status is dropped, which is the one ordering that matters here — the
            // scaffolded version dropped it first and would have silently backfilled every row as
            // "Active, open-ended".
            //
            //   Status 0 Active     -> not suspended, window open
            //   Status 1 OnLeave    -> SUSPENDED, window open        (WM's invention; no legacy counterpart)
            //   Status 2 Terminated -> not suspended, EmployedUntil = date(UpdatedAt ?? CreatedAt)
            //   anything else       -> suspended, window open        (fail closed, as 001 P2's does)
            //
            // Mirrors StatusMap above; EmploymentMigrationTests asserts the two agree.
            //
            // `AT TIME ZONE 'UTC'` before the cast is not decoration: CreatedAt/UpdatedAt are
            // `timestamptz`, and casting one straight to `date` resolves against the session's
            // TimeZone, so the same database would migrate differently depending on who ran it.
            migrationBuilder.Sql("""
                UPDATE people."Employees"
                SET "IsSuspended" = CASE "Status"
                        WHEN 0 THEN false
                        WHEN 1 THEN true
                        WHEN 2 THEN false
                        ELSE true
                    END,
                    "EmployedUntil" = CASE
                        WHEN "Status" = 2
                        THEN CAST(COALESCE("UpdatedAt", "CreatedAt") AT TIME ZONE 'UTC' AS date)
                        ELSE NULL
                    END;
                """);

            // Last: with the window populated, the enum has nothing left to say. EmployeeStatus
            // survives in the domain as a DERIVED label and is never stored again, so the two can no
            // longer disagree — which is the defect dbo.ActiveEmployeesView demonstrates.
            migrationBuilder.DropColumn(
                name: "Status",
                schema: "people",
                table: "Employees");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible in shape, lossy in content, and the loss is one-directional by construction —
        /// see <see cref="StatusFrom"/>. Rolling back restores a schema the old code can run against;
        /// it does not restore information this migration never had (the true leaving date) nor keep
        /// information only the new schema can hold (a future-dated leaver, a leaving reason, leaver
        /// comments, the distinction between suspended-and-leaving and merely leaving).
        ///
        /// <para>
        /// One cosmetic residue, measured rather than assumed: <c>Status</c> comes back as the
        /// <i>last</i> column rather than the eleventh, because Postgres appends a re-added column.
        /// Nothing reads columns by ordinal — EF names them — and there is no way to avoid it short of
        /// rebuilding the table. Verified by diffing <c>\d people."Employees"</c> before and after a
        /// real up/down cycle: ordering is the only line that differs.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // First, so the restore below has somewhere to write.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "people",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            //   window ends -> 2 Terminated   (kept over suspension: leaving is the stronger fact)
            //   suspended   -> 1 OnLeave
            //   otherwise   -> 0 Active       (including a future EmployedFrom, which 0 cannot express)
            //
            // Mirrors StatusFrom above; EmploymentMigrationTests round-trips StatusMap through it.
            migrationBuilder.Sql("""
                UPDATE people."Employees"
                SET "Status" = CASE
                        WHEN "EmployedUntil" IS NOT NULL THEN 2
                        WHEN "IsSuspended" THEN 1
                        ELSE 0
                    END;
                """);

            // AddColumn needed a default to populate the existing rows, but the column this restores
            // never had one (20260720080022_Initial:47). Dropping it leaves the pre-migration schema
            // byte for byte rather than approximately — measured by rolling this migration back
            // against Postgres and diffing `\d people."Employees"`, which is how the difference was
            // noticed in the first place.
            migrationBuilder.Sql("""
                ALTER TABLE people."Employees" ALTER COLUMN "Status" DROP DEFAULT;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_LeavingReasons_LeavingReasonId",
                schema: "people",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "LeavingReasons",
                schema: "people");

            migrationBuilder.DropIndex(
                name: "IX_Employees_LeavingReasonId",
                schema: "people",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployedUntil",
                schema: "people",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                schema: "people",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LeaverComments",
                schema: "people",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LeavingReasonId",
                schema: "people",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "EmployedFrom",
                schema: "people",
                table: "Employees",
                newName: "HireDate");
        }
    }
}
