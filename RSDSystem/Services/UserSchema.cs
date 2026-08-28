using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public static class UserSchema
    {
        public static void Ensure(PayrollDbContext db)
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Users', N'MustChangePassword') IS NULL
    ALTER TABLE dbo.Users ADD MustChangePassword bit NOT NULL
        CONSTRAINT DF_Users_MustChangePassword DEFAULT(0);");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Users', N'PasswordResetTokenHash') IS NULL
    ALTER TABLE dbo.Users ADD PasswordResetTokenHash nvarchar(64) NULL;");

            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Users', N'PasswordResetExpiry') IS NULL
    ALTER TABLE dbo.Users ADD PasswordResetExpiry datetime2 NULL;");
        }
    }
}
