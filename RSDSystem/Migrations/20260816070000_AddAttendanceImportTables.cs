using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RSDSystem.Models;

#nullable disable

namespace RSDSystem.Migrations
{
    [DbContext(typeof(PayrollDbContext))]
    [Migration("20260816070000_AddAttendanceImportTables")]
    public partial class AddAttendanceImportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceImports",
                columns: table => new
                {
                    AttendanceImportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceImports", x => x.AttendanceImportId);
                    table.ForeignKey(
                        name: "FK_AttendanceImports_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    AttendanceRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceImportId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    ExternalUserId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeIn1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeOut1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeIn2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeOut2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OvertimeIn = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OvertimeOut = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WorkHoursNormal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    WorkHoursActual = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    LateMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    AbsenceDays = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Matched = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.AttendanceRecordId);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceImports_AttendanceImportId",
                        column: x => x.AttendanceImportId,
                        principalTable: "AttendanceImports",
                        principalColumn: "AttendanceImportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceImports_ProjectId",
                table: "AttendanceImports",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_AttendanceImportId",
                table: "AttendanceRecords",
                column: "AttendanceImportId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId",
                table: "AttendanceRecords",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AttendanceRecords");
            migrationBuilder.DropTable(name: "AttendanceImports");
        }
    }
}
