using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class ActivityLogSchema
    {
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.ActivityLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ActivityLogs] (
        [ActivityLogId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ActivityLogs] PRIMARY KEY,
        [UserId] int NULL,
        [UserName] nvarchar(150) NOT NULL,
        [Role] nvarchar(30) NOT NULL,
        [Activity] nvarchar(60) NOT NULL,
        [Module] nvarchar(40) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [ProjectId] int NULL,
        [RelatedId] int NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ActivityLogs_CreatedAt] DEFAULT(SYSDATETIME())
    );
    CREATE INDEX [IX_ActivityLogs_CreatedAt]
        ON [dbo].[ActivityLogs]([CreatedAt] DESC);
    CREATE INDEX [IX_ActivityLogs_Module_CreatedAt]
        ON [dbo].[ActivityLogs]([Module], [CreatedAt] DESC);
END");
        }
    }
}
