using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.TimeAttendance.Data.Migrations
{
    /// <summary>
    /// 002 P1: the Clocking row — one per employee per day, unique on <c>(EmployeeId, Date)</c> (legacy
    /// <c>IX_Clockings_EmployeeDate</c>, <c>28.V2.2.0.sql:6411</c>). No foreign key to People
    /// (invariant 1) or to <c>DayTemplates</c> (a dangling id must stay reportable, S3). This migration
    /// fills nothing: <c>ClockingBackfill</c> creates a row for every day that has punches on the next
    /// start, from each punch's frozen <c>LocalDate</c>. Down drops the table; punches are untouched.
    /// </summary>
    public partial class ClockingRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clockings",
                schema: "time_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    ExceptionsMuted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clockings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_EmployeeId_Date",
                schema: "time_attendance",
                table: "Clockings",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clockings",
                schema: "time_attendance");
        }
    }
}
