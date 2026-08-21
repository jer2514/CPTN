using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class PayrollSchema
    {
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

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'RegularHours') IS NULL
    ALTER TABLE dbo.Payrolls ADD RegularHours decimal(18,2) NOT NULL CONSTRAINT DF_Payrolls_RegularHours DEFAULT(0);");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'RegularHours') IS NOT NULL
    UPDATE dbo.Payrolls
    SET RegularHours = CAST(RegularDaysWorked AS decimal(18,2)) * 8
    WHERE RegularHours = 0 AND RegularDaysWorked > 0;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.ProjectEmployeeHistories', N'U') IS NULL
AND OBJECT_ID(N'dbo.Projects', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.ProjectEmployeeHistories (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProjectId int NOT NULL,
        EmployeeId int NOT NULL,
        RecordedAt datetime2 NOT NULL,
        CONSTRAINT FK_ProjectEmployeeHistories_Projects
            FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId) ON DELETE CASCADE,
        CONSTRAINT FK_ProjectEmployeeHistories_Employees
            FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX IX_ProjectEmployeeHistories_ProjectId_EmployeeId
        ON dbo.ProjectEmployeeHistories(ProjectId, EmployeeId);
END");
        }
    }
}
