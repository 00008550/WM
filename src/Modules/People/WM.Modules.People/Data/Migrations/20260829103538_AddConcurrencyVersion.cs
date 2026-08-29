using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.People.Data.Migrations
{
    /// <summary>
    /// The optimistic-concurrency token (011 P5). An ordinary <c>uuid</c> column, deliberately, and
    /// not Npgsql's <c>xmin</c> — see <c>WM.SharedKernel.Domain.AuditableEntity.Version</c> for why
    /// a system column would make every concurrency test pass vacuously.
    /// </summary>
    public partial class AddConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                schema: "people",
                table: "Employees",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill, and it is not cosmetic. AddColumn stamps every existing row with the all-zero
            // uuid; the PUT handler refuses an echoed empty token with a 400, so without this line
            // every employee that predates the upgrade would be uneditable until someone happened to
            // save them — which is impossible, because saving them is what is refused. Distinct
            // values, not one shared constant, so two rows are never each other's valid token.
            migrationBuilder.Sql("""UPDATE people."Employees" SET "Version" = gen_random_uuid();""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                schema: "people",
                table: "Employees");
        }
    }
}
