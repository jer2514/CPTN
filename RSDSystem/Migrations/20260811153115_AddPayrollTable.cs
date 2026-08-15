using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payrolls",
                columns: table => new
                {
                    PayrollId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    PayPeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RegularDaysWorked = table.Column<int>(type: "int", nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AbsentDays = table.Column<int>(type: "int", nullable: false),
                    RegularPay = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    OvertimePay = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    GrossPay = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    CashAdvance = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    NetPay = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GeneratedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payrolls", x => x.PayrollId);
                    table.ForeignKey(
                        name: "FK_Payrolls_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payrolls_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payrolls_EmployeeId",
                table: "Payrolls",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payrolls_ProjectId",
                table: "Payrolls",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payrolls");
        }
    }
}
