using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RSDSystem.Models;

#nullable disable

namespace RSDSystem.Migrations
{
    [DbContext(typeof(PayrollDbContext))]
    [Migration("20260816070000_AddAttendanceImportTables")]
    public partial class AddAttendanceImportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same tables may already exist from Program.cs startup SQL.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NULL
BEGIN
    CREATE TABLE [AttendanceImports] (
        [AttendanceImportId] int NOT NULL IDENTITY,
        [ProjectId] int NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [Format] nvarchar(30) NOT NULL,
        [PeriodStart] datetime2 NULL,
        [PeriodEnd] datetime2 NULL,
        [ImportedBy] nvarchar(150) NULL,
        [ImportedAt] datetime2 NOT NULL,
        [RowCount] int NOT NULL,
        CONSTRAINT [PK_AttendanceImports] PRIMARY KEY ([AttendanceImportId]),
        CONSTRAINT [FK_AttendanceImports_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_AttendanceImports_ProjectId] ON [AttendanceImports] ([ProjectId]);
END

IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE [AttendanceRecords] (
        [AttendanceRecordId] int NOT NULL IDENTITY,
        [AttendanceImportId] int NOT NULL,
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
        CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY ([AttendanceRecordId]),
        CONSTRAINT [FK_AttendanceRecords_AttendanceImports_AttendanceImportId] FOREIGN KEY ([AttendanceImportId]) REFERENCES [AttendanceImports] ([AttendanceImportId]) ON DELETE CASCADE,
        CONSTRAINT [FK_AttendanceRecords_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_AttendanceRecords_AttendanceImportId] ON [AttendanceRecords] ([AttendanceImportId]);
    CREATE INDEX [IX_AttendanceRecords_EmployeeId] ON [AttendanceRecords] ([EmployeeId]);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
    DROP TABLE [AttendanceRecords];
IF OBJECT_ID(N'dbo.AttendanceImports', N'U') IS NOT NULL
    DROP TABLE [AttendanceImports];
");
        }
    }
}
