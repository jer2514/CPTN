using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    /// <summary>Creates AttendanceImports / AttendanceRecords tables if this database never had them.</summary>
    public static class AttendanceSchema
    {
        /// <summary>
        /// Creates AttendanceImports / AttendanceRecords if missing and patches columns/indexes on older SQL Server databases.
        /// Called at app startup and again before a file import so hosted DBs (Somee) gain ProjectId without a full migration.
        /// </summary>
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

            // ProjectId must be patched in its own batches. SQL Server cannot ADD a
            // column and UPDATE it in the same batch, and a failed combined patch
            // would leave hosted databases (Somee) without ProjectId.
            ApplyOptionalSql(db, ProjectIdPatchSqls);

            try
            {
                db.Database.ExecuteSqlRaw(PatchColumnsSql);
            }
            catch
            {
                // Tables are already usable; skip optional column patches.
            }

            ApplyOptionalSql(db, ProjectIdPatchSqls);
            PatchUniqueDateIndex(db);
        }

        /// <summary>
        /// Async version of <see cref="Ensure"/> used by AttendanceImportService right before SaveChanges on an import batch.
        /// </summary>
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

            await ApplyOptionalSqlAsync(db, ProjectIdPatchSqls);

            try
            {
                await db.Database.ExecuteSqlRawAsync(PatchColumnsSql);
            }
            catch
            {
                // Tables are already usable; skip optional column patches.
            }

            await ApplyOptionalSqlAsync(db, ProjectIdPatchSqls);
            await PatchUniqueDateIndexAsync(db);
        }

        /// <summary>
        /// Runs each SQL batch in its own try/catch so a failed ALTER on one hosted database does not block import or startup.
        /// </summary>
        private static void ApplyOptionalSql(PayrollDbContext db, IEnumerable<string> batches)
        {
            foreach (var sql in batches)
            {
                try
                {
                    db.Database.ExecuteSqlRaw(sql);
                }
                catch
                {
                    // Optional schema patches must not block import or startup.
                }
            }
        }

        /// <summary>
        /// Async optional patches (ProjectId add/backfill, unique employee+date index). Failures are ignored on purpose.
        /// </summary>
        private static async Task ApplyOptionalSqlAsync(PayrollDbContext db, IEnumerable<string> batches)
        {
            foreach (var sql in batches)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql);
                }
                catch
                {
                    // Optional schema patches must not block import or startup.
                }
            }
        }

        /// <summary>
        /// Drops old unique constraints, nulls dummy dates, deletes duplicate employee+date rows, then creates a filtered unique index.
        /// Prevents two imported punches for the same employee on the same work date.
        /// </summary>
        private static void PatchUniqueDateIndex(PayrollDbContext db) =>
            ApplyOptionalSql(db, UniqueDateIndexSqls);

        /// <summary>
        /// Async unique-date index patch used from <see cref="EnsureAsync"/> during import.
        /// </summary>
        private static Task PatchUniqueDateIndexAsync(PayrollDbContext db) =>
            ApplyOptionalSqlAsync(db, UniqueDateIndexSqls);

        private const string CreateImportsSql = @"
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceImports] (
        [AttendanceImportId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceImports] PRIMARY KEY,
        [ProjectId] int NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [Format] nvarchar(30) NOT NULL,
        [PeriodStart] datetime2 NULL,
        [PeriodEnd] datetime2 NULL,
        [ImportedBy] nvarchar(150) NULL,
        [ImportedAt] datetime2 NOT NULL,
        [RowCount] int NOT NULL,
        CONSTRAINT [FK_AttendanceImports_Projects_ProjectId]
            FOREIGN KEY ([ProjectId]) REFERENCES [dbo].[Projects]([ProjectId]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_AttendanceImports_ProjectId] ON [dbo].[AttendanceImports]([ProjectId]);
END";

        // NO ACTION on EmployeeId avoids SQL Server 'multiple cascade paths' which
        // otherwise leaves AttendanceRecords missing after AttendanceImports is created.
        private const string CreateRecordsSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceRecords] (
        [AttendanceRecordId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY,
        [AttendanceImportId] int NOT NULL,
        [ProjectId] int NOT NULL,
        [EmployeeId] int NULL,
        [ExternalUserId] nvarchar(40) NOT NULL,
        [EmployeeName] nvarchar(150) NOT NULL,
        [WorkDate] datetime2 NULL,
        [PeriodStart] datetime2 NULL,
        [PeriodEnd] datetime2 NULL,
        [TimeIn1] nvarchar(40) NULL,
        [TimeOut1] nvarchar(40) NULL,
        [TimeIn2] nvarchar(40) NULL,
        [TimeOut2] nvarchar(40) NULL,
        [OvertimeIn] nvarchar(40) NULL,
        [OvertimeOut] nvarchar(40) NULL,
        [WorkHoursNormal] decimal(10,2) NOT NULL,
        [WorkHoursActual] decimal(10,2) NOT NULL,
        [LateMinutes] int NOT NULL,
        [EarlyMinutes] int NOT NULL,
        [OvertimeHours] decimal(10,2) NOT NULL,
        [AbsenceDays] decimal(10,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Matched] bit NOT NULL,
        CONSTRAINT [FK_AttendanceRecords_AttendanceImports_AttendanceImportId]
            FOREIGN KEY ([AttendanceImportId]) REFERENCES [dbo].[AttendanceImports]([AttendanceImportId]) ON DELETE CASCADE,
        CONSTRAINT [FK_AttendanceRecords_Employees_EmployeeId]
            FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees]([EmployeeId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_AttendanceRecords_AttendanceImportId] ON [dbo].[AttendanceRecords]([AttendanceImportId]);
    CREATE INDEX [IX_AttendanceRecords_EmployeeId] ON [dbo].[AttendanceRecords]([EmployeeId]);
END";

        private const string CreateRecordsWithoutEmployeeFkSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceRecords] (
        [AttendanceRecordId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY,
        [AttendanceImportId] int NOT NULL,
        [ProjectId] int NOT NULL,
        [EmployeeId] int NULL,
        [ExternalUserId] nvarchar(40) NOT NULL,
        [EmployeeName] nvarchar(150) NOT NULL,
        [WorkDate] datetime2 NULL,
        [PeriodStart] datetime2 NULL,
        [PeriodEnd] datetime2 NULL,
        [TimeIn1] nvarchar(40) NULL,
        [TimeOut1] nvarchar(40) NULL,
        [TimeIn2] nvarchar(40) NULL,
        [TimeOut2] nvarchar(40) NULL,
        [OvertimeIn] nvarchar(40) NULL,
        [OvertimeOut] nvarchar(40) NULL,
        [WorkHoursNormal] decimal(10,2) NOT NULL,
        [WorkHoursActual] decimal(10,2) NOT NULL,
        [LateMinutes] int NOT NULL,
        [EarlyMinutes] int NOT NULL,
        [OvertimeHours] decimal(10,2) NOT NULL,
        [AbsenceDays] decimal(10,2) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Matched] bit NOT NULL,
        CONSTRAINT [FK_AttendanceRecords_AttendanceImports_AttendanceImportId]
            FOREIGN KEY ([AttendanceImportId]) REFERENCES [dbo].[AttendanceImports]([AttendanceImportId]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_AttendanceRecords_AttendanceImportId] ON [dbo].[AttendanceRecords]([AttendanceImportId]);
    CREATE INDEX [IX_AttendanceRecords_EmployeeId] ON [dbo].[AttendanceRecords]([EmployeeId]);
END";

        private const string PatchColumnsSql = @"
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AttendanceImports', N'FileName') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [FileName] nvarchar(260) NOT NULL CONSTRAINT [DF_AttendanceImports_FileName] DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'Source') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [Source] nvarchar(20) NOT NULL CONSTRAINT [DF_AttendanceImports_Source] DEFAULT(N'Manual');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'Format') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [Format] nvarchar(30) NOT NULL CONSTRAINT [DF_AttendanceImports_Format] DEFAULT(N'Daily');
    IF COL_LENGTH(N'dbo.AttendanceImports', N'RowCount') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [RowCount] int NOT NULL CONSTRAINT [DF_AttendanceImports_RowCount] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceImports', N'ImportedBy') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [ImportedBy] nvarchar(150) NULL;
    IF COL_LENGTH(N'dbo.AttendanceImports', N'ImportedAt') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [ImportedAt] datetime2 NOT NULL CONSTRAINT [DF_AttendanceImports_ImportedAt] DEFAULT(SYSDATETIME());
    IF COL_LENGTH(N'dbo.AttendanceImports', N'PeriodStart') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [PeriodStart] datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceImports', N'PeriodEnd') IS NULL
        ALTER TABLE [dbo].[AttendanceImports] ADD [PeriodEnd] datetime2 NULL;
END

IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'ExternalUserId') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [ExternalUserId] nvarchar(40) NOT NULL CONSTRAINT [DF_AttendanceRecords_ExternalUserId] DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EmployeeName') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [EmployeeName] nvarchar(150) NOT NULL CONSTRAINT [DF_AttendanceRecords_EmployeeName] DEFAULT(N'');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [WorkDate] datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'PeriodStart') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [PeriodStart] datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'PeriodEnd') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [PeriodEnd] datetime2 NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [TimeIn1] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [TimeOut1] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [TimeIn2] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [TimeOut2] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [OvertimeIn] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [OvertimeOut] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkHoursNormal') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [WorkHoursNormal] decimal(10,2) NOT NULL CONSTRAINT [DF_AttendanceRecords_WorkHoursNormal] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkHoursActual') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [WorkHoursActual] decimal(10,2) NOT NULL CONSTRAINT [DF_AttendanceRecords_WorkHoursActual] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'LateMinutes') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [LateMinutes] int NOT NULL CONSTRAINT [DF_AttendanceRecords_LateMinutes] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EarlyMinutes') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [EarlyMinutes] int NOT NULL CONSTRAINT [DF_AttendanceRecords_EarlyMinutes] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeHours') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [OvertimeHours] decimal(10,2) NOT NULL CONSTRAINT [DF_AttendanceRecords_OvertimeHours] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'AbsenceDays') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [AbsenceDays] decimal(10,2) NOT NULL CONSTRAINT [DF_AttendanceRecords_AbsenceDays] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'Status') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_AttendanceRecords_Status] DEFAULT(N'Incomplete');
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'Matched') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [Matched] bit NOT NULL CONSTRAINT [DF_AttendanceRecords_Matched] DEFAULT(0);
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'EmployeeId') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [EmployeeId] int NULL;

    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn1') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [TimeIn1] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut1') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [TimeOut1] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeIn2') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [TimeIn2] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'TimeOut2') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [TimeOut2] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeIn') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [OvertimeIn] nvarchar(40) NULL;
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') IS NOT NULL AND COL_LENGTH(N'dbo.AttendanceRecords', N'OvertimeOut') < 80
        ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [OvertimeOut] nvarchar(40) NULL;
END";

        // Each statement is its own batch so ADD ProjectId is visible to the later UPDATE.
        private static readonly string[] ProjectIdPatchSqls =
        {
            @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.AttendanceRecords', N'ProjectId') IS NULL
    ALTER TABLE [dbo].[AttendanceRecords] ADD [ProjectId] int NULL;",
            @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.AttendanceRecords', N'ProjectId') IS NOT NULL
AND OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NOT NULL
    UPDATE r SET r.[ProjectId] = i.[ProjectId]
    FROM [dbo].[AttendanceRecords] r
    INNER JOIN [dbo].[AttendanceImports] i ON i.[AttendanceImportId] = r.[AttendanceImportId]
    WHERE r.[ProjectId] IS NULL;",
            @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.AttendanceRecords', N'ProjectId') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM [dbo].[AttendanceRecords] WHERE [ProjectId] IS NULL)
AND EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.AttendanceRecords')
      AND name = N'ProjectId'
      AND is_nullable = 1)
    ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [ProjectId] int NOT NULL;"
        };

        // Each statement runs in its own batch so ALTER COLUMN is visible to later UPDATEs.
        private static readonly string[] UniqueDateIndexSqls =
        {
            DropUniqueDateIndexSql,
            MakeWorkDateNullableSql,
            MakeLegacyDateNullableSql,
            CleanDummyDatesSql,
            DeleteDuplicateWorkDatesSql,
            CreateFilteredUniqueDateIndexSql
        };

        private const string DropUniqueDateIndexSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE name = N'IX_AttendanceRecords_EmployeeId_Date'
          AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
        ALTER TABLE [dbo].[AttendanceRecords] DROP CONSTRAINT [IX_AttendanceRecords_EmployeeId_Date];

    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE name = N'IX_AttendanceRecords_EmployeeId_WorkDate'
          AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
        ALTER TABLE [dbo].[AttendanceRecords] DROP CONSTRAINT [IX_AttendanceRecords_EmployeeId_WorkDate];

    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE name = N'UQ_AttendanceRecords_EmployeeId_Date'
          AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
        ALTER TABLE [dbo].[AttendanceRecords] DROP CONSTRAINT [UQ_AttendanceRecords_EmployeeId_Date];

    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE name = N'UQ_AttendanceRecords_EmployeeId_WorkDate'
          AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
        ALTER TABLE [dbo].[AttendanceRecords] DROP CONSTRAINT [UQ_AttendanceRecords_EmployeeId_WorkDate];

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_AttendanceRecords_EmployeeId_Date'
          AND object_id = OBJECT_ID(N'dbo.AttendanceRecords')
          AND is_unique_constraint = 0)
        DROP INDEX [IX_AttendanceRecords_EmployeeId_Date] ON [dbo].[AttendanceRecords];

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_AttendanceRecords_EmployeeId_WorkDate'
          AND object_id = OBJECT_ID(N'dbo.AttendanceRecords')
          AND is_unique_constraint = 0)
        DROP INDEX [IX_AttendanceRecords_EmployeeId_WorkDate] ON [dbo].[AttendanceRecords];
END";

        private const string MakeWorkDateNullableSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID(N'dbo.AttendanceRecords'), N'WorkDate', 'AllowsNull') = 0
    ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [WorkDate] datetime2 NULL;";

        private const string MakeLegacyDateNullableSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AttendanceRecords', N'Date') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID(N'dbo.AttendanceRecords'), N'Date', 'AllowsNull') = 0
    ALTER TABLE [dbo].[AttendanceRecords] ALTER COLUMN [Date] datetime2 NULL;";

        private const string CleanDummyDatesSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NOT NULL
       AND COLUMNPROPERTY(OBJECT_ID(N'dbo.AttendanceRecords'), N'WorkDate', 'AllowsNull') = 1
        UPDATE [dbo].[AttendanceRecords] SET [WorkDate] = NULL WHERE [WorkDate] < '19000101';

    IF COL_LENGTH(N'dbo.AttendanceRecords', N'Date') IS NOT NULL
       AND COLUMNPROPERTY(OBJECT_ID(N'dbo.AttendanceRecords'), N'Date', 'AllowsNull') = 1
        UPDATE [dbo].[AttendanceRecords] SET [Date] = NULL WHERE [Date] < '19000101';
END";

        private const string DeleteDuplicateWorkDatesSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NOT NULL
    DELETE FROM [dbo].[AttendanceRecords]
    WHERE [AttendanceRecordId] IN (
        SELECT [AttendanceRecordId]
        FROM (
            SELECT [AttendanceRecordId],
                ROW_NUMBER() OVER (
                    PARTITION BY [EmployeeId], CAST([WorkDate] AS date)
                    ORDER BY [AttendanceRecordId] DESC
                ) AS rn
            FROM [dbo].[AttendanceRecords]
            WHERE [EmployeeId] IS NOT NULL AND [WorkDate] IS NOT NULL AND [WorkDate] >= '19000101'
        ) ranked
        WHERE rn > 1
    );";

        private const string CreateFilteredUniqueDateIndexSql = @"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AttendanceRecords', N'EmployeeId') IS NOT NULL
   AND COL_LENGTH(N'dbo.AttendanceRecords', N'WorkDate') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_AttendanceRecords_EmployeeId_Date'
          AND object_id = OBJECT_ID(N'dbo.AttendanceRecords'))
    CREATE UNIQUE INDEX [IX_AttendanceRecords_EmployeeId_Date]
        ON [dbo].[AttendanceRecords]([EmployeeId], [WorkDate])
        WHERE [EmployeeId] IS NOT NULL AND [WorkDate] IS NOT NULL;";
    }
}
