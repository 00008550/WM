using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.People.Data.Migrations
{
    /// <summary>
    /// <c>Employee.DepartmentId</c> becomes a real reference (007 P4): an index and a foreign key to
    /// <c>Departments</c>, <c>ON DELETE RESTRICT</c>.
    ///
    /// <para>
    /// <b>Unlike 007 P3, this migration does NOT fail on bad data — it repairs it and says how much.</b>
    /// A <c>DepartmentId</c> naming no department cannot be honoured by anyone: there is no department
    /// to restore it to, and the only thing it does today is fail every department-scope check. So it
    /// is set to <c>NULL</c> ("no department", which <c>WithinScope</c> already treats as invisible to a
    /// department scope), and the count is raised as a <c>WARNING</c> so the operator running the
    /// migration sees it in the output.
    /// </para>
    /// <para>
    /// A department that <i>exists</i> but belongs to another site is <b>reported, not changed</b>. It
    /// does not violate the foreign key, and nulling it would silently take the person out of sight of
    /// every department-scoped manager who sees them today — a data-scope decision, not a migration's.
    /// The endpoints refuse such a pairing from P4 on, so the next edit of that record forces the fix.
    /// </para>
    /// </summary>
    public partial class EmployeeDepartmentReference : Migration
    {
        public const string ForeignKeyName = "FK_Employees_Departments_DepartmentId";

        /// <summary>Nulls dangling department references and reports how many; reports mis-sited ones.</summary>
        public const string RepairOrphansSql = """
            DO $$
            DECLARE orphaned integer;
            DECLARE missited integer;
            BEGIN
                UPDATE people."Employees" e
                SET "DepartmentId" = NULL
                WHERE e."DepartmentId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM people."Departments" d WHERE d."Id" = e."DepartmentId");
                GET DIAGNOSTICS orphaned = ROW_COUNT;
                RAISE WARNING '007 P4: % employee(s) referred to a department that does not exist; their DepartmentId was set to NULL.', orphaned;

                SELECT count(*) INTO missited
                FROM people."Employees" e
                JOIN people."Departments" d ON d."Id" = e."DepartmentId"
                WHERE d."SiteId" <> e."SiteId";
                IF missited > 0 THEN
                    RAISE WARNING '007 P4: % employee(s) have a department belonging to a different site. Left unchanged; the next edit of each will be refused until the department is corrected.', missited;
                END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Repair first: the foreign key below would refuse to be created over a dangling row.
            migrationBuilder.Sql(RepairOrphansSql);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                schema: "people",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: ForeignKeyName,
                schema: "people",
                table: "Employees",
                column: "DepartmentId",
                principalSchema: "people",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The nulled references are not restored: the departments they named do not exist, so
            // there is nothing correct to restore them to.
            migrationBuilder.DropForeignKey(
                name: ForeignKeyName,
                schema: "people",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DepartmentId",
                schema: "people",
                table: "Employees");
        }
    }
}
