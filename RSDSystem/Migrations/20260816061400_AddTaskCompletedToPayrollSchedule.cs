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
            // Startup SQL may have already added this column when Database.Migrate() was off.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PayrollSchedules', N'TaskCompleted') IS NULL
BEGIN
    ALTER TABLE [PayrollSchedules] ADD [TaskCompleted] bit NOT NULL CONSTRAINT [DF_PayrollSchedules_TaskCompleted] DEFAULT CAST(0 AS bit);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.PayrollSchedules', N'TaskCompleted') IS NOT NULL
BEGIN
    DECLARE @df sysname;
    SELECT @df = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE [d].[parent_object_id] = OBJECT_ID(N'dbo.PayrollSchedules') AND [c].[name] = N'TaskCompleted';
    IF @df IS NOT NULL EXEC(N'ALTER TABLE [PayrollSchedules] DROP CONSTRAINT [' + @df + N'];');
    ALTER TABLE [PayrollSchedules] DROP COLUMN [TaskCompleted];
END
");
        }
    }
}
