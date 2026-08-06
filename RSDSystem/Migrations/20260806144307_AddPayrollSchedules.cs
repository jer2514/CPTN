using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserCode",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Employees_EmployeeCode",
                table: "Employees");

            migrationBuilder.CreateTable(
                name: "PayrollSchedules",
                columns: table => new
                {
                    PayrollScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    TypeOfService = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSchedules", x => x.PayrollScheduleId);
                    table.ForeignKey(
                        name: "FK_PayrollSchedules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSchedules_ProjectId",
                table: "PayrollSchedules",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserCode",
                table: "Users",
                column: "UserCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_EmployeeCode",
                table: "Employees",
                column: "EmployeeCode",
                unique: true);
        }
    }
}
