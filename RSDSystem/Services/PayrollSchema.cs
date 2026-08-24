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

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'SubmittedAt') IS NULL
    ALTER TABLE dbo.Payrolls ADD SubmittedAt datetime2 NULL;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Payrolls', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Payrolls', N'SubmittedAt') IS NOT NULL
    UPDATE dbo.Payrolls
    SET SubmittedAt = GeneratedDate
    WHERE SubmittedAt IS NULL
      AND (Status = N'Submitted' OR Status = N'Approved');");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Employees', N'DailyRate') IS NOT NULL
AND COL_LENGTH(N'dbo.Employees', N'RatePerHour') IS NOT NULL
    UPDATE dbo.Employees
    SET RatePerHour = ROUND(DailyRate / 8.0, 2)
    WHERE DailyRate > 0;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.PayrollPredictionHistories', N'U') IS NULL
AND OBJECT_ID(N'dbo.Projects', N'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.PayrollPredictionHistories (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProjectId int NOT NULL,
        PreviousMonth1 datetime2 NOT NULL,
        PreviousAmount1 decimal(18,2) NOT NULL,
        PreviousMonth2 datetime2 NOT NULL,
        PreviousAmount2 decimal(18,2) NOT NULL,
        PredictionMonth datetime2 NOT NULL,
        PredictionLabel nvarchar(40) NOT NULL,
        PredictedPayroll decimal(18,2) NOT NULL,
        AllocatedBudget decimal(18,2) NOT NULL,
        HasAllocatedBudget bit NOT NULL,
        BudgetDifference decimal(18,2) NOT NULL,
        ExceedsBudget bit NOT NULL,
        UnusualChange bit NOT NULL,
        ChangePercent decimal(18,2) NOT NULL,
        RiskTitle nvarchar(80) NULL,
        RiskDetail nvarchar(300) NULL,
        Engine nvarchar(20) NOT NULL,
        GeneratedAt datetime2 NOT NULL,
        CONSTRAINT FK_PayrollPredictionHistories_Projects
            FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId) ON DELETE CASCADE
    );
    CREATE INDEX IX_PayrollPredictionHistories_ProjectId_GeneratedAt
        ON dbo.PayrollPredictionHistories(ProjectId, GeneratedAt);
END");
        }
    }
}
