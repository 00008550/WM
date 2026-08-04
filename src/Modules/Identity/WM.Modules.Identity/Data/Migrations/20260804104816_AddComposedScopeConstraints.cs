using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComposedScopeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuleKind",
                schema: "identity",
                table: "SecurityGroups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SecurityGroupConstraint",
                schema: "identity",
                columns: table => new
                {
                    SecurityGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    IncludeDescendants = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroupConstraint", x => new { x.SecurityGroupId, x.Dimension });
                    table.ForeignKey(
                        name: "FK_SecurityGroupConstraint_SecurityGroups_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalSchema: "identity",
                        principalTable: "SecurityGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityGroupConstraintValue",
                schema: "identity",
                columns: table => new
                {
                    SecurityGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    ValueId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroupConstraintValue", x => new { x.SecurityGroupId, x.Dimension, x.ValueId });
                    table.ForeignKey(
                        name: "FK_SecurityGroupConstraintValue_SecurityGroupConstraint_Securi~",
                        columns: x => new { x.SecurityGroupId, x.Dimension },
                        principalSchema: "identity",
                        principalTable: "SecurityGroupConstraint",
                        principalColumns: new[] { "SecurityGroupId", "Dimension" },
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- data migration ----
            //
            // Translate every existing group's legacy single-kind scope into the composed shape.
            // This must preserve each group's meaning EXACTLY: a migration that silently widens
            // one group's visibility is the failure mode for this change.
            //
            // Enum values are written as literals deliberately. Referencing the C# enums here
            // would let a future rename or reordering silently rewrite historical data.
            //   DataScopeKind : None=0, Self=1, Departments=2, Sites=3, All=4
            //   ScopeRuleKind : None=0, Self=1, Constrained=2, All=3
            //   ScopeDimension: Site=0, Department=1

            migrationBuilder.Sql("""
                UPDATE identity."SecurityGroups"
                SET "RuleKind" = CASE "ScopeKind"
                    WHEN 4 THEN 3   -- All        -> All
                    WHEN 1 THEN 1   -- Self       -> Self
                    WHEN 3 THEN 2   -- Sites      -> Constrained
                    WHEN 2 THEN 2   -- Departments-> Constrained
                    ELSE 0          -- None, or anything unrecognised -> grants nothing
                END;
                """);

            // Site-scoped groups. The constraint row is inserted even when the group has no
            // sites: under the old model an empty list matched nobody, and a present-but-empty
            // dimension matches nobody under the new one. Dropping the row would also grant
            // nothing, but recording it keeps the group's intent legible.
            //
            // IncludeChildSites was group-wide; it lands on the Site dimension, which is where
            // it belongs now that one group can no longer expand another group's sites.
            migrationBuilder.Sql("""
                INSERT INTO identity."SecurityGroupConstraint" ("SecurityGroupId", "Dimension", "IncludeDescendants")
                SELECT "Id", 0, "IncludeChildSites"
                FROM identity."SecurityGroups"
                WHERE "ScopeKind" = 3;
                """);

            migrationBuilder.Sql("""
                INSERT INTO identity."SecurityGroupConstraintValue" ("SecurityGroupId", "Dimension", "ValueId")
                SELECT s."SecurityGroupId", 0, s."SiteId"
                FROM identity."SecurityGroupSite" s
                JOIN identity."SecurityGroups" g ON g."Id" = s."SecurityGroupId"
                WHERE g."ScopeKind" = 3;
                """);

            // Department-scoped groups. Departments are not hierarchical in WM, so descendant
            // expansion never applied and is recorded as false rather than inherited.
            migrationBuilder.Sql("""
                INSERT INTO identity."SecurityGroupConstraint" ("SecurityGroupId", "Dimension", "IncludeDescendants")
                SELECT "Id", 1, false
                FROM identity."SecurityGroups"
                WHERE "ScopeKind" = 2;
                """);

            migrationBuilder.Sql("""
                INSERT INTO identity."SecurityGroupConstraintValue" ("SecurityGroupId", "Dimension", "ValueId")
                SELECT d."SecurityGroupId", 1, d."DepartmentId"
                FROM identity."SecurityGroupDepartment" d
                JOIN identity."SecurityGroups" g ON g."Id" = d."SecurityGroupId"
                WHERE g."ScopeKind" = 2;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Genuinely reversible. The data migration only ever wrote into structures this method
        /// drops — <c>ScopeKind</c>, <c>IncludeChildSites</c>, <c>SecurityGroupSite</c> and
        /// <c>SecurityGroupDepartment</c> are read but never modified — so rolling back restores
        /// the exact prior state rather than an approximation of it.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityGroupConstraintValue",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SecurityGroupConstraint",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "RuleKind",
                schema: "identity",
                table: "SecurityGroups");
        }
    }
}
