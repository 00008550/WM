using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.TimeAttendance.Data.Migrations
{
    /// <inheritdoc />
    public partial class DayTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DayTemplates",
                schema: "time_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    NightShiftEndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    OffsetAfterTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    OffsetTransactionToNextDay = table.Column<bool>(type: "boolean", nullable: false),
                    AllocateToPreviousDayWindow = table.Column<TimeSpan>(type: "interval", nullable: true),
                    OverriddenByMasterTemplate = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MasterTemplateAssignments",
                schema: "time_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MasterTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterTemplateAssignments", x => x.Id);
                    table.CheckConstraint("ck_master_template_assignments_window", "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"StartDate\" <= \"EndDate\"");
                    table.ForeignKey(
                        name: "FK_MasterTemplateAssignments_DayTemplates_MasterTemplateId",
                        column: x => x.MasterTemplateId,
                        principalSchema: "time_attendance",
                        principalTable: "DayTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftMatchingRules",
                schema: "time_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DayTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchType = table.Column<int>(type: "integer", nullable: false),
                    StartTimeFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    StartTimeTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTimeFrom = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndTimeTo = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    TemplateToAssignId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftMatchingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftMatchingRules_DayTemplates_DayTemplateId",
                        column: x => x.DayTemplateId,
                        principalSchema: "time_attendance",
                        principalTable: "DayTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftMatchingRules_DayTemplates_TemplateToAssignId",
                        column: x => x.TemplateToAssignId,
                        principalSchema: "time_attendance",
                        principalTable: "DayTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DayTemplates_Code",
                schema: "time_attendance",
                table: "DayTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterTemplateAssignments_EmployeeId",
                schema: "time_attendance",
                table: "MasterTemplateAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_MasterTemplateAssignments_MasterTemplateId",
                schema: "time_attendance",
                table: "MasterTemplateAssignments",
                column: "MasterTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftMatchingRules_DayTemplateId",
                schema: "time_attendance",
                table: "ShiftMatchingRules",
                column: "DayTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftMatchingRules_TemplateToAssignId",
                schema: "time_attendance",
                table: "ShiftMatchingRules",
                column: "TemplateToAssignId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasterTemplateAssignments",
                schema: "time_attendance");

            migrationBuilder.DropTable(
                name: "ShiftMatchingRules",
                schema: "time_attendance");

            migrationBuilder.DropTable(
                name: "DayTemplates",
                schema: "time_attendance");
        }
    }
}
