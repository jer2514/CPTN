using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAndUserCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserCode",
                table: "Users",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmployeeCode",
                table: "Employees",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Backfill unique codes for existing rows before enforcing uniqueness
            migrationBuilder.Sql(@"
        WITH Numbered AS (
            SELECT UserId, ROW_NUMBER() OVER (ORDER BY UserId) AS rn
            FROM Users
        )
        UPDATE u
        SET u.UserCode = RIGHT(CAST(YEAR(GETDATE()) AS varchar(4)), 2)
                        + RIGHT('0000' + CAST(n.rn AS varchar(4)), 4)
        FROM Users u
        INNER JOIN Numbered n ON u.UserId = n.UserId;
    ");

            migrationBuilder.Sql(@"
        WITH Numbered AS (
            SELECT EmployeeId, ROW_NUMBER() OVER (ORDER BY EmployeeId) AS rn
            FROM Employees
        )
        UPDATE e
        SET e.EmployeeCode = RIGHT(CAST(YEAR(GETDATE()) AS varchar(4)), 2)
                            + RIGHT('0000' + CAST(n.rn AS varchar(4)), 4)
        FROM Employees e
        INNER JOIN Numbered n ON e.EmployeeId = n.EmployeeId;
    ");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserCode",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Employees_EmployeeCode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "UserCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmployeeCode",
                table: "Employees");
        }
    }
}
