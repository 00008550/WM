using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.TimeAttendance.Data.Migrations
{
    /// <summary>
    /// 008 P5: a punch keeps the server's receipt instant beside the client's, the offset the client
    /// wrote, and its flags. Existing punches were all stamped by the server or seeded as terminal
    /// punches, so their receipt instant <b>is</b> their <c>Timestamp</c>: the column is added
    /// nullable, filled from it, then made NOT NULL — no row is left with a sentinel. They have no
    /// client offset (null) and no flags (0). Down drops the three columns; the instant is untouched.
    /// </summary>
    public partial class PunchReceivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientUtcOffsetMinutes",
                schema: "time_attendance",
                table: "Punches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Flags",
                schema: "time_attendance",
                table: "Punches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAt",
                schema: "time_attendance",
                table: "Punches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE time_attendance.\"Punches\" SET \"ReceivedAt\" = \"Timestamp\";");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReceivedAt",
                schema: "time_attendance",
                table: "Punches",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientUtcOffsetMinutes",
                schema: "time_attendance",
                table: "Punches");

            migrationBuilder.DropColumn(
                name: "Flags",
                schema: "time_attendance",
                table: "Punches");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                schema: "time_attendance",
                table: "Punches");
        }
    }
}
