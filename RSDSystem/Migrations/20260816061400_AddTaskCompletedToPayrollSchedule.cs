using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RSDSystem.Models;

#nullable disable

namespace RSDSystem.Migrations
{
    [DbContext(typeof(PayrollDbContext))]
    [Migration("20260816061400_AddTaskCompletedToPayrollSchedule")]
    public partial class AddTaskCompletedToPayrollSchedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TaskCompleted",
                table: "PayrollSchedules",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskCompleted",
                table: "PayrollSchedules");
        }
    }
}
