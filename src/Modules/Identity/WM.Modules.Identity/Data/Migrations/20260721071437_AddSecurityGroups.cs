using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WM.Modules.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityGroups",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    ScopeKind = table.Column<int>(type: "integer", nullable: false),
                    IncludeChildSites = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityGroupDepartment",
                schema: "identity",
                columns: table => new
                {
                    SecurityGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroupDepartment", x => new { x.SecurityGroupId, x.DepartmentId });
                    table.ForeignKey(
                        name: "FK_SecurityGroupDepartment_SecurityGroups_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalSchema: "identity",
                        principalTable: "SecurityGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityGroupSite",
                schema: "identity",
                columns: table => new
                {
                    SecurityGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroupSite", x => new { x.SecurityGroupId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_SecurityGroupSite_SecurityGroups_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalSchema: "identity",
                        principalTable: "SecurityGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSecurityGroup",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityGroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSecurityGroup", x => new { x.UserId, x.SecurityGroupId });
                    table.ForeignKey(
                        name: "FK_UserSecurityGroup_SecurityGroups_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalSchema: "identity",
                        principalTable: "SecurityGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSecurityGroup_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityGroups_Name",
                schema: "identity",
                table: "SecurityGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSecurityGroup_SecurityGroupId",
                schema: "identity",
                table: "UserSecurityGroup",
                column: "SecurityGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityGroupDepartment",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SecurityGroupSite",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserSecurityGroup",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SecurityGroups",
                schema: "identity");
        }
    }
}
