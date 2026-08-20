using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RSDSystem.Models;

#nullable disable

namespace RSDSystem.Migrations
{
    [DbContext(typeof(PayrollDbContext))]
    [Migration("20260819193000_AddPayrollScheduleIdToPayroll")]
    public partial class AddPayrollScheduleIdToPayroll : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NULL
    ALTER TABLE [Payrolls] ADD [PayrollScheduleId] int NULL;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND OBJECT_ID(N'dbo.PayrollSchedules', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.FK_Payrolls_PayrollSchedules_PayrollScheduleId', N'F') IS NULL
    ALTER TABLE [Payrolls] ADD CONSTRAINT [FK_Payrolls_PayrollSchedules_PayrollScheduleId]
        FOREIGN KEY ([PayrollScheduleId]) REFERENCES [PayrollSchedules] ([PayrollScheduleId]) ON DELETE SET NULL;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    CREATE INDEX [IX_Payrolls_PayrollScheduleId] ON [Payrolls] ([PayrollScheduleId]);
");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
    UPDATE p
    SET PayrollScheduleId = s.PayrollScheduleId
    FROM [Payrolls] p
    CROSS APPLY (
        SELECT TOP 1 sch.PayrollScheduleId
        FROM [PayrollSchedules] sch
        WHERE sch.ProjectId = p.ProjectId
          AND CAST(p.PayPeriodStart AS date) >= CAST(sch.StartingDate AS date)
          AND CAST(p.PayPeriodEnd AS date) <= CAST(sch.EndDate AS date)
        ORDER BY sch.StartingDate DESC, sch.PayrollScheduleId DESC
    ) s
    WHERE p.PayrollScheduleId IS NULL;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_EmployeeId_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    CREATE UNIQUE INDEX [IX_Payrolls_EmployeeId_PayrollScheduleId]
        ON [Payrolls] ([EmployeeId], [PayrollScheduleId])
        WHERE [PayrollScheduleId] IS NOT NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_EmployeeId_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    DROP INDEX [IX_Payrolls_EmployeeId_PayrollScheduleId] ON [Payrolls];

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    DROP INDEX [IX_Payrolls_PayrollScheduleId] ON [Payrolls];

IF OBJECT_ID(N'dbo.FK_Payrolls_PayrollSchedules_PayrollScheduleId', N'F') IS NOT NULL
    ALTER TABLE [Payrolls] DROP CONSTRAINT [FK_Payrolls_PayrollSchedules_PayrollScheduleId];

IF COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
    ALTER TABLE [Payrolls] DROP COLUMN [PayrollScheduleId];
");
        }
    }
}
