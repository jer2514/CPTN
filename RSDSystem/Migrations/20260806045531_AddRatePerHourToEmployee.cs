using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddRatePerHourToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RatePerHour",
                table: "Employees",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatePerHour",
                table: "Employees");
        }
    }
}
