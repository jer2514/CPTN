using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class AttendanceSchema
    {
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(CreateImportsSql);
            try
            {
                db.Database.ExecuteSqlRaw(CreateRecordsSql);
            }
            catch
            {
                db.Database.ExecuteSqlRaw(CreateRecordsWithoutEmployeeFkSql);
            }

            db.Database.ExecuteSqlRaw(PatchColumnsSql);
        }

        public static async Task EnsureAsync(PayrollDbContext db, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await db.Database.ExecuteSqlRawAsync(CreateImportsSql);
            try
            {
                await db.Database.ExecuteSqlRawAsync(CreateRecordsSql);
            }
            catch
            {
                await db.Database.ExecuteSqlRawAsync(CreateRecordsWithoutEmployeeFkSql);
            }

            await db.Database.ExecuteSqlRawAsync(PatchColumnsSql);
        }

        private const string CreateImportsSql = @"
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceImports (
        AttendanceImportId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceImports PRIMARY KEY,
        ProjectId int NOT NULL,
        FileName nvarchar(260) NOT NULL,
        Source nvarchar(20) NOT NULL,
        Format nvarchar(30) NOT NULL,
        PeriodStart datetime2 NULL,
        PeriodEnd datetime2 NULL,
        ImportedBy nvarchar(150) NULL,
        ImportedAt datetime2 NOT NULL,
        RowCount int NOT NULL,
        CONSTRAINT FK_AttendanceImports_Projects_ProjectId
            FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(ProjectId) ON DELETE CASCADE
    );
    CREATE INDEX IX_AttendanceImports_ProjectId ON dbo.AttendanceImports(ProjectId);
END";

        // NO ACTION on EmployeeId avoids SQL Server 'multiple cascade paths' which
        // otherwise leaves AttendanceRecords missing after AttendanceImports is created.
        private const string CreateRecordsSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceRecords (
        AttendanceRecordId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
        AttendanceImportId int NOT NULL,
        EmployeeId int NULL,
        ExternalUserId nvarchar(40) NOT NULL,
        EmployeeName nvarchar(150) NOT NULL,
        WorkDate datetime2 NULL,
        PeriodStart datetime2 NULL,
        PeriodEnd datetime2 NULL,
        TimeIn1 nvarchar(40) NULL,
        TimeOut1 nvarchar(40) NULL,
        TimeIn2 nvarchar(40) NULL,
        TimeOut2 nvarchar(40) NULL,
        OvertimeIn nvarchar(40) NULL,
        OvertimeOut nvarchar(40) NULL,
        WorkHoursNormal decimal(10,2) NOT NULL,
        WorkHoursActual decimal(10,2) NOT NULL,
        LateMinutes int NOT NULL,
        EarlyMinutes int NOT NULL,
        OvertimeHours decimal(10,2) NOT NULL,
        AbsenceDays decimal(10,2) NOT NULL,
        Status nvarchar(20) NOT NULL,
        Matched bit NOT NULL,
        CONSTRAINT FK_AttendanceRecords_AttendanceImports_AttendanceImportId
            FOREIGN KEY (AttendanceImportId) REFERENCES dbo.AttendanceImports(AttendanceImportId) ON DELETE CASCADE,
        CONSTRAINT FK_AttendanceRecords_Employees_EmployeeId
            FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId) ON DELETE NO ACTION
    );
    CREATE INDEX IX_AttendanceRecords_AttendanceImportId ON dbo.AttendanceRecords(AttendanceImportId);
    CREATE INDEX IX_AttendanceRecords_EmployeeId ON dbo.AttendanceRecords(EmployeeId);
END";

        private const string CreateRecordsWithoutEmployeeFkSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceRecords (
        AttendanceRecordId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
        AttendanceImportId int NOT NULL,
        EmployeeId int NULL,
        ExternalUserId nvarchar(40) NOT NULL,
        EmployeeName nvarchar(150) NOT NULL,
        WorkDate datetime2 NULL,
        PeriodStart datetime2 NULL,
        PeriodEnd datetime2 NULL,
        TimeIn1 nvarchar(40) NULL,
        TimeOut1 nvarchar(40) NULL,
        TimeIn2 nvarchar(40) NULL,
        TimeOut2 nvarchar(40) NULL,
        OvertimeIn nvarchar(40) NULL,
        OvertimeOut nvarchar(40) NULL,
        WorkHoursNormal decimal(10,2) NOT NULL,
        WorkHoursActual decimal(10,2) NOT NULL,
        LateMinutes int NOT NULL,
        EarlyMinutes int NOT NULL,
        OvertimeHours decimal(10,2) NOT NULL,
        AbsenceDays decimal(10,2) NOT NULL,
        Status nvarchar(20) NOT NULL,
        Matched bit NOT NULL,
        CONSTRAINT FK_AttendanceRecords_AttendanceImports_AttendanceImportId
            FOREIGN KEY (AttendanceImportId) REFERENCES dbo.AttendanceImports(AttendanceImportId) ON DELETE CASCADE
    );
    CREATE INDEX IX_AttendanceRecords_AttendanceImportId ON dbo.AttendanceRecords(AttendanceImportId);
    CREATE INDEX IX_AttendanceRecords_EmployeeId ON dbo.AttendanceRecords(EmployeeId);
END";

        private const string PatchColumnsSql = @"
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AttendanceImports', N'FileName') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD FileName nvarchar(260) NOT NULL CONSTRAINT DF_AttendanceImports_FileName DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'Source') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD Source nvarchar(20) NOT NULL CONSTRAINT DF_AttendanceImports_Source DEFAULT(N'Manual');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'Format') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD Format nvarchar(30) NOT NULL CONSTRAINT DF_AttendanceImports_Format DEFAULT(N'Daily');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'RowCount') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD RowCount int NOT NULL CONSTRAINT DF_AttendanceImports_RowCount DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceImports', N'ImportedBy') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD ImportedBy nvarchar(150) NULL;
    IF COL_LENGTH(N'dbo.AttendanceImports', N'ImportedAt') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD ImportedAt datetime2 NOT NULL CONSTRAINT DF_AttendanceImports_ImportedAt DEFAULT(SYSDATETIME());
    IF COL_LENGTH(N'dbo.AttendanceImports', N'PeriodStart') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD PeriodStart datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceImports', N'PeriodEnd') IS NULL
        ALTER TABLE dbo.AttendanceImports ADD PeriodEnd datetime2 NULL;
END

IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'ExternalUserId') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD ExternalUserId nvarchar(40) NOT NULL CONSTRAINT DF_AttendanceRecords_ExternalUserId DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EmployeeName') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD EmployeeName nvarchar(150) NOT NULL CONSTRAINT DF_AttendanceRecords_EmployeeName DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD WorkDate datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'PeriodStart') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD PeriodStart datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'PeriodEnd') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD PeriodEnd datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD TimeIn1 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD TimeOut1 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD TimeIn2 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD TimeOut2 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD OvertimeIn nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD OvertimeOut nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkHoursNormal') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD WorkHoursNormal decimal(10,2) NOT NULL CONSTRAINT DF_AttendanceRecords_WorkHoursNormal DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkHoursActual') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD WorkHoursActual decimal(10,2) NOT NULL CONSTRAINT DF_AttendanceRecords_WorkHoursActual DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'LateMinutes') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD LateMinutes int NOT NULL CONSTRAINT DF_AttendanceRecords_LateMinutes DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EarlyMinutes') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD EarlyMinutes int NOT NULL CONSTRAINT DF_AttendanceRecords_EarlyMinutes DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeHours') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD OvertimeHours decimal(10,2) NOT NULL CONSTRAINT DF_AttendanceRecords_OvertimeHours DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'AbsenceDays') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD AbsenceDays decimal(10,2) NOT NULL CONSTRAINT DF_AttendanceRecords_AbsenceDays DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'Status') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD Status nvarchar(20) NOT NULL CONSTRAINT DF_AttendanceRecords_Status DEFAULT(N'Incomplete');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'Matched') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD Matched bit NOT NULL CONSTRAINT DF_AttendanceRecords_Matched DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EmployeeId') IS NULL
        ALTER TABLE dbo.AttendanceRecords ADD EmployeeId int NULL;

    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN TimeIn1 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN TimeOut1 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN TimeIn2 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN TimeOut2 nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN OvertimeIn nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') < 80
        ALTER TABLE dbo.AttendanceRecords ALTER COLUMN OvertimeOut nvarchar(40) NULL;
END";
    }
}
