using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.TimeAttendance.Data.Migrations
{
    /// <summary>
    /// 008 P4: a punch carries the local day it belongs to, frozen at record time, and the zone it was
    /// resolved in. Both columns are <b>nullable and this migration fills nothing</b>: the zone is
    /// People's (site → ancestor → installation default) and the installation default is
    /// configuration, so resolving it here would mean reading <c>people."Sites"</c> from
    /// TimeAttendance's migration — the cross-module read invariant 1 forbids — and guessing the
    /// default. <c>PunchLocalDateBackfill</c> fills every null row through <c>ISiteTimeZones</c> on
    /// the next start, before the host serves a request. Down drops both columns; the instant in
    /// <c>Timestamp</c> is untouched either way.
    /// </summary>
    public partial class PunchLocalDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LocalDate",
                schema: "time_attendance",
                table: "Punches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalZone",
                schema: "time_attendance",
                table: "Punches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Punches_EmployeeId_LocalDate",
                schema: "time_attendance",
                table: "Punches",
                columns: new[] { "EmployeeId", "LocalDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Punches_EmployeeId_LocalDate",
                schema: "time_attendance",
                table: "Punches");

            migrationBuilder.DropColumn(
                name: "LocalDate",
                schema: "time_attendance",
                table: "Punches");

            migrationBuilder.DropColumn(
                name: "LocalZone",
                schema: "time_attendance",
                table: "Punches");
        }
    }
}
