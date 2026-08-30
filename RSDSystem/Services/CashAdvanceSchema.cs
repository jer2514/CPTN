using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class CashAdvanceSchema
    {
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.CashAdvances', N'U') IS NULL
AND OBJECT_ID(N'dbo.Projects', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.CashAdvances (
        CashAdvanceId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProjectId int NOT NULL,
        EmployeeId int NOT NULL,
        AdvanceDate datetime2 NOT NULL,
        Amount decimal(12,2) NOT NULL,
        Reason nvarchar(400) NULL,
        Status nvarchar(30) NOT NULL,
        CreatedAt datetime2 NOT NULL,
        CreatedBy nvarchar(150) NULL,
        MarkedAt datetime2 NULL,
        MarkedBy nvarchar(150) NULL,
        PayrollId int NULL,
        DeductedAt datetime2 NULL
    );
    CREATE INDEX IX_CashAdvances_ProjectId ON dbo.CashAdvances(ProjectId);
    CREATE INDEX IX_CashAdvances_EmployeeId ON dbo.CashAdvances(EmployeeId);
    CREATE INDEX IX_CashAdvances_Status ON dbo.CashAdvances(Status);
END");
        }
    }
}
