using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RSDSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionReasonToPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Payrolls",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Payrolls");
        }
    }
}
