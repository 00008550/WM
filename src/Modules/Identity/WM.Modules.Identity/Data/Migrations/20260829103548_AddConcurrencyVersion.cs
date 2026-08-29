using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.Identity.Data.Migrations
{
    /// <summary>
    /// The optimistic-concurrency token (011 P5), on both auditable entities in this schema. An
    /// ordinary <c>uuid</c>, never <c>xmin</c> — see <c>AuditableEntity.Version</c>.
    /// <c>SecurityGroups</c> gets the column because it inherits it; only <c>Users</c> is configured
    /// as a concurrency token, and <c>IdentityDbContext</c> says why.
    /// </summary>
    public partial class AddConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                schema: "identity",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                schema: "identity",
                table: "SecurityGroups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill: AddColumn stamps every existing row with the all-zero uuid, and UpdateAsync
            // refuses an echoed empty token — so without this line every user that predates the
            // upgrade would be uneditable, permanently. Distinct values per row, so no two rows are
            // each other's valid token. SecurityGroups is backfilled too even though its column is
            // not yet enforced: a half-populated column is a trap for whoever enforces it later.
            migrationBuilder.Sql("""UPDATE identity."Users" SET "Version" = gen_random_uuid();""");
            migrationBuilder.Sql("""UPDATE identity."SecurityGroups" SET "Version" = gen_random_uuid();""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "identity",
                table: "SecurityGroups");
        }
    }
}
