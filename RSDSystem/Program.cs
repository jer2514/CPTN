using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RSDSystem.Filters;
using RSDSystem.Models;
using RSDSystem.Services;
using System;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<AuthCheckFilter>();
});

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "RSDSystemAuth";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<AttendanceImportService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

Console.WriteLine("== DEBUG: Using connection string = " + builder.Configuration.GetConnectionString("DefaultConnection"));

var app = builder.Build();

// Seed demo users so you can login from any device
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<PayrollDbContext>();

        // Optional: apply migrations automatically in development - remove for production deployments
        //try
        //{
        //    db.Database.Migrate();
        //}
        //catch
        //{
        //    // ignore migration errors in local dev if DB unavailable
        //}

        try
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Projects', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Projects')
          AND name = N'Status' AND is_nullable = 0
    )
        ALTER TABLE dbo.Projects ALTER COLUMN Status nvarchar(max) NULL;

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Projects')
          AND name = N'ProjectName' AND is_nullable = 0
    )
        ALTER TABLE dbo.Projects ALTER COLUMN ProjectName nvarchar(150) NULL;

    UPDATE dbo.Projects SET Status = N'On Going' WHERE Status IS NULL OR LTRIM(RTRIM(Status)) = N'' OR Status = N'Active';
    UPDATE dbo.Projects SET Status = N'Finished' WHERE Status = N'Completed';
    UPDATE dbo.Projects SET Status = N'On Hold' WHERE Status = N'Cancelled';
    UPDATE dbo.Projects SET ProjectName = N'' WHERE ProjectName IS NULL;
END");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Project null-column fix error: " + ex.Message);
        }

        try
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.Employees', N'Email') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Employees')
          AND name = N'Email'
          AND is_nullable = 0
    )
    BEGIN
        IF EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = N'IX_Employees_Email'
              AND object_id = OBJECT_ID(N'dbo.Employees')
        )
            DROP INDEX IX_Employees_Email ON dbo.Employees;

        ALTER TABLE dbo.Employees ALTER COLUMN Email nvarchar(100) NULL;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_Employees_Email'
          AND object_id = OBJECT_ID(N'dbo.Employees')
    )
    BEGIN
        CREATE UNIQUE INDEX IX_Employees_Email ON dbo.Employees(Email)
        WHERE [Email] IS NOT NULL AND [Email] <> N'';
    END
END");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Employee email schema fix error: " + ex.Message);
        }

        try
        {
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.PayrollSchedules', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.PayrollSchedules', N'TaskCompleted') IS NULL
BEGIN
    ALTER TABLE dbo.PayrollSchedules ADD TaskCompleted bit NOT NULL CONSTRAINT DF_PayrollSchedules_TaskCompleted DEFAULT(0);
END");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Payroll schedule task column fix error: " + ex.Message);
        }

        try
        {
            db.Database.ExecuteSqlRaw(@"
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
END

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
        TimeIn1 nvarchar(20) NULL,
        TimeOut1 nvarchar(20) NULL,
        TimeIn2 nvarchar(20) NULL,
        TimeOut2 nvarchar(20) NULL,
        OvertimeIn nvarchar(20) NULL,
        OvertimeOut nvarchar(20) NULL,
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
            FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId) ON DELETE SET NULL
    );
    CREATE INDEX IX_AttendanceRecords_AttendanceImportId ON dbo.AttendanceRecords(AttendanceImportId);
    CREATE INDEX IX_AttendanceRecords_EmployeeId ON dbo.AttendanceRecords(EmployeeId);
END");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Attendance table create error: " + ex.Message);
        }

        var toAdd = new List<User>();

        if (!db.Users.Any(u => u.Username == "demo"))
        {
            toAdd.Add(new User
            {
                FirstName = "Demo",
                LastName = "User",
                Username = "demo",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Email = "demo@example.com",
                ContactNumber = "09123456789",
                Address = "Demo account",
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.Now
            });
        }

        if (!db.Users.Any(u => u.Username == "payroll"))
        {
            toAdd.Add(new User
            {
                FirstName = "Payroll",
                LastName = "Staff",
                Username = "payroll",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Payroll@123"),
                Email = "payroll@example.com",
                ContactNumber = "09987654321",
                Address = "Payroll demo account",
                Role = "PayrollStaff",
                IsActive = true,
                CreatedAt = DateTime.Now
            });
        }

        if (toAdd.Count > 0)
        {
            db.Users.AddRange(toAdd);
            db.SaveChanges();
            Console.WriteLine($"Seeded {toAdd.Count} demo user(s).");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Seeding error: " + ex.Message);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

// Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();