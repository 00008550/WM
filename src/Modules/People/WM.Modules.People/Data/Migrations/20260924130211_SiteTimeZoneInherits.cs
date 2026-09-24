using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.People.Data.Migrations
{
    /// <summary>
    /// Plan 008 P2: <c>Site.TimeZone</c> becomes nullable, and null means <b>inherit</b> — nearest
    /// ancestor site with a zone, else the installation default (<c>Time:InstallationZone</c>).
    ///
    /// <para>
    /// <c>"UTC"</c> stops being a default that means "nobody decided". Until now the entity
    /// initialised the column to <c>"UTC"</c>, so a stored <c>'UTC'</c> cannot be told apart from an
    /// unset one; it is nulled here and so inherits. An installation that really means UTC sets it
    /// again, explicitly, through <c>PUT /api/sites/{id}/time-zone</c>. Rows that are blank or not a
    /// zone Postgres knows (a typo, a Windows id — WM is IANA only) are nulled for the same reason:
    /// nothing reads the column yet, so nothing depended on the bad value. Each count is reported
    /// with <c>RAISE NOTICE</c> rather than failing the migration.
    /// </para>
    /// </summary>
    public partial class SiteTimeZoneInherits : Migration
    {
        internal const string NormaliseSql = """
            DO $$
            DECLARE defaulted integer; unusable integer;
            BEGIN
                UPDATE people."Sites" SET "TimeZone" = NULL WHERE "TimeZone" = 'UTC';
                GET DIAGNOSTICS defaulted = ROW_COUNT;
                UPDATE people."Sites" SET "TimeZone" = NULL
                WHERE "TimeZone" IS NOT NULL
                  AND "TimeZone" NOT IN (SELECT name FROM pg_timezone_names);
                GET DIAGNOSTICS unusable = ROW_COUNT;
                RAISE NOTICE '008 P2: % site(s) held the old ''UTC'' default and now inherit; % held an unusable zone and now inherit.', defaulted, unusable;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first: the normalisation writes NULL.
            migrationBuilder.AlterColumn<string>(
                name: "TimeZone",
                schema: "people",
                table: "Sites",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql(NormaliseSql);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Lossy by design: "inherit" has no spelling in the old schema, so every unset site goes back
        /// to <c>'UTC'</c> — exactly what the old entity default would have written.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE people."Sites" SET "TimeZone" = 'UTC' WHERE "TimeZone" IS NULL;""");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZone",
                schema: "people",
                table: "Sites",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
