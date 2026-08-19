using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class NotificationSchema
    {
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(CreateNotificationsSql);
            db.Database.ExecuteSqlRaw(CreateCorrectionsSql);
        }

        private const string CreateNotificationsSql = @"
IF OBJECT_ID(N'dbo.AppNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AppNotifications] (
        [AppNotificationId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AppNotifications] PRIMARY KEY,
        [RecipientRole] nvarchar(30) NOT NULL,
        [RecipientName] nvarchar(150) NULL,
        [Kind] nvarchar(60) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [ProjectId] int NULL,
        [RelatedId] int NULL,
        [Url] nvarchar(250) NULL,
        [IsRead] bit NOT NULL CONSTRAINT [DF_AppNotifications_IsRead] DEFAULT(0),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_AppNotifications_CreatedAt] DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_AppNotifications_Role_Name_Created]
        ON [dbo].[AppNotifications]([RecipientRole], [RecipientName], [CreatedAt] DESC);
END";

        private const string CreateCorrectionsSql = @"
IF OBJECT_ID(N'dbo.AttendanceCorrectionRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AttendanceCorrectionRequests] (
        [AttendanceCorrectionRequestId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceCorrectionRequests] PRIMARY KEY,
        [AttendanceRecordId] int NOT NULL,
        [ProjectId] int NOT NULL,
        [EmployeeId] int NULL,
        [EmployeeName] nvarchar(150) NOT NULL,
        [PayrollStaffName] nvarchar(150) NOT NULL,
        [WorkDate] datetime2 NULL,
        [TimeIn1] nvarchar(40) NULL,
        [TimeOut1] nvarchar(40) NULL,
        [TimeIn2] nvarchar(40) NULL,
        [TimeOut2] nvarchar(40) NULL,
        [OvertimeIn] nvarchar(40) NULL,
        [OvertimeOut] nvarchar(40) NULL,
        [Reason] nvarchar(250) NOT NULL CONSTRAINT [DF_AttendanceCorrectionRequests_Reason] DEFAULT(N''),
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_AttendanceCorrectionRequests_Status] DEFAULT(N'Pending'),
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_AttendanceCorrectionRequests_CreatedAt] DEFAULT(SYSDATETIME()),
        [ReviewedAt] datetime2 NULL,
        [ReturnReason] nvarchar(250) NULL
    );
    CREATE INDEX [IX_AttendanceCorrectionRequests_Record]
        ON [dbo].[AttendanceCorrectionRequests]([AttendanceRecordId], [Status]);
    CREATE INDEX [IX_AttendanceCorrectionRequests_Project]
        ON [dbo].[AttendanceCorrectionRequests]([ProjectId], [Status]);
END";
    }
}
