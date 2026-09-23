using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.People.Data.Migrations
{
    /// <summary>
    /// One employee-code rule, held by the database (007 P3). The case-sensitive unique index on
    /// <c>Code</c> is replaced by a unique index on <c>lower("Code")</c>, so the constraint and the
    /// rule the endpoints state — <c>E1030</c> and <c>e1030</c> are one badge number — are the same
    /// thing. Before this, two concurrent creates differing only by case both passed the pre-check and
    /// both inserted.
    ///
    /// <para>
    /// <b>THIS MIGRATION FAILS ON PURPOSE</b> if the table already holds two codes that differ only by
    /// case. It does not pick a winner: which of two people keeps a badge number is an HR decision, and
    /// silently renaming or dropping one would re-point every punch filed under it. The guard raises
    /// with the colliding codes named; an operator resolves them and re-runs. Leading zeros are NOT
    /// folded — <c>42</c> and <c>0042</c> stay distinct (plan 007, open question 1).
    /// </para>
    /// </summary>
    public partial class EmployeeCodeCaseInsensitive : Migration
    {
        public const string IndexName = "UX_Employees_Code_Lower";

        /// <summary>Refuses to proceed while any two codes collide case-insensitively, and says which.</summary>
        public const string CollisionGuardSql = """
            DO $$
            DECLARE collisions text;
            BEGIN
                SELECT string_agg(codes, '; ') INTO collisions
                FROM (
                    SELECT string_agg("Code", ', ' ORDER BY "Code") AS codes
                    FROM people."Employees"
                    GROUP BY lower("Code")
                    HAVING count(*) > 1
                ) AS dupes;
                IF collisions IS NOT NULL THEN
                    RAISE EXCEPTION 'Employee codes collide case-insensitively and must be resolved by hand before this migration can run: %', collisions
                        USING ERRCODE = 'unique_violation';
                END IF;
            END $$;
            """;

        public const string CreateIndexSql =
            $"""CREATE UNIQUE INDEX "{IndexName}" ON people."Employees" (lower("Code"));""";

        public const string DropIndexSql = $"""DROP INDEX people."{IndexName}";""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard, then the new constraint, THEN drop the old one — so the table is never without a
            // uniqueness constraint, even for the length of one statement.
            migrationBuilder.Sql(CollisionGuardSql);
            migrationBuilder.Sql(CreateIndexSql);

            migrationBuilder.DropIndex(
                name: "IX_Employees_Code",
                schema: "people",
                table: "Employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The case-sensitive index is strictly weaker, so restoring it cannot fail on data this
            // migration let in.
            migrationBuilder.CreateIndex(
                name: "IX_Employees_Code",
                schema: "people",
                table: "Employees",
                column: "Code",
                unique: true);

            migrationBuilder.Sql(DropIndexSql);
        }
    }
}
