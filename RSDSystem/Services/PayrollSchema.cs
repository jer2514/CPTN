using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    /// <summary>Adds missing Payroll.PayrollScheduleId on older databases at startup.</summary>
    public static class PayrollSchema
    {
        /// <summary>
        /// Adds Payroll.PayrollScheduleId, FK, indexes, and back-fills slips from matching schedule dates on older databases.
        /// Startup uses this so generate-per-schedule and unique employee+schedule payroll rows work without a new migration.
        /// </summary>
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NULL
    ALTER TABLE dbo.Payrolls ADD PayrollScheduleId int NULL;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND OBJECT_ID(N'dbo.PayrollSchedules', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.FK_Payrolls_PayrollSchedules_PayrollScheduleId', N'F') IS NULL
    ALTER TABLE dbo.Payrolls ADD CONSTRAINT FK_Payrolls_PayrollSchedules_PayrollScheduleId
        FOREIGN KEY (PayrollScheduleId) REFERENCES dbo.PayrollSchedules(PayrollScheduleId) ON DELETE SET NULL;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    CREATE INDEX IX_Payrolls_PayrollScheduleId ON dbo.Payrolls(PayrollScheduleId);");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
    UPDATE p
    SET PayrollScheduleId = s.PayrollScheduleId
    FROM dbo.Payrolls p
    CROSS APPLY (
        SELECT TOP 1 sch.PayrollScheduleId
        FROM dbo.PayrollSchedules sch
        WHERE sch.ProjectId = p.ProjectId
          AND CAST(p.PayPeriodStart AS date) >= CAST(sch.StartingDate AS date)
          AND CAST(p.PayPeriodEnd AS date) <= CAST(sch.EndDate AS date)
        ORDER BY sch.StartingDate DESC, sch.PayrollScheduleId DESC
    ) s
    WHERE p.PayrollScheduleId IS NULL;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'PayrollScheduleId') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Payrolls_EmployeeId_PayrollScheduleId'
      AND object_id = OBJECT_ID(N'dbo.Payrolls')
)
    CREATE UNIQUE INDEX IX_Payrolls_EmployeeId_PayrollScheduleId
        ON dbo.Payrolls(EmployeeId, PayrollScheduleId)
        WHERE PayrollScheduleId IS NOT NULL;");
        }
    }
}
