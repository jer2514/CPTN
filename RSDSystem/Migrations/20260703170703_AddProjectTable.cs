using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DailyRate",
                table: "Employees",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Employees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TypeOfService = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimateEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayrollBudget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PayrollDistribution = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AssignedPayrollStaff = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMonthlyBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    MonthYear = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MonthDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMonthlyBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectMonthlyBudgets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_ProjectId",
                table: "Employees",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMonthlyBudgets_ProjectId",
                table: "ProjectMonthlyBudgets",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Projects_ProjectId",
                table: "Employees",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Projects_ProjectId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "ProjectMonthlyBudgets");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Employees_ProjectId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "DailyRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Employees");
        }
    }
}
