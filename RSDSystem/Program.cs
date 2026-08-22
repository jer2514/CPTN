using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RSDSystem.Filters;
using RSDSystem.Models;
using RSDSystem.Services;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHttpClient("PayrollPrediction", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddScoped<PayrollPredictionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ActivityLogService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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

        try
        {
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Database migrate error: " + ex.Message);
        }

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
END

IF OBJECT_ID(N'dbo.PayrollSchedules', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.PayrollSchedules', N'TaskApproved') IS NULL
BEGIN
    ALTER TABLE dbo.PayrollSchedules ADD TaskApproved bit NOT NULL CONSTRAINT DF_PayrollSchedules_TaskApproved DEFAULT(0);
    EXEC(N'UPDATE dbo.PayrollSchedules SET TaskApproved = 1 WHERE TaskCompleted = 1');
END");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Payroll schedule task column fix error: " + ex.Message);
        }

        try
        {
            PayrollSchema.Ensure(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Payroll schedule link schema fix error: " + ex.Message);
        }

        try
        {
            AttendanceSchema.Ensure(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Attendance schema fix error: " + ex.Message);
        }

        try
        {
            NotificationSchema.Ensure(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Notification schema fix error: " + ex.Message);
        }

        try
        {
            ActivityLogSchema.Ensure(db);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Activity log schema fix error: " + ex.Message);
        }

        // Change these two values, then restart the app. The existing Admin
        // row is updated so login matches Program.cs (not only first-time seed).
        const string seedAdminUsername = "admin";
        const string seedAdminPassword = "Demo@123";

        var admin = db.Users.FirstOrDefault(u => u.Role == "Admin")
            ?? db.Users.FirstOrDefault(u => u.Username == seedAdminUsername);

        if (admin == null)
        {
            var year = DateTime.Now.ToString("yy");
            db.Users.Add(new User
            {
                FirstName = "Admin",
                LastName = "User",
                Username = seedAdminUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedAdminPassword),
                Email = "admin@example.com",
                ContactNumber = "09123456789",
                Address = "Demo account",
                Role = "Admin",
                UserCode = year + "0001",
                IsActive = true,
                CreatedAt = DateTime.Now
            });
            db.SaveChanges();
            Console.WriteLine("Seeded admin user from Program.cs.");
        }
        else
        {
            var usernameTaken = db.Users.Any(u =>
                u.UserId != admin.UserId && u.Username == seedAdminUsername);
            if (!usernameTaken && admin.Username != seedAdminUsername)
                admin.Username = seedAdminUsername;

            var passwordMatches = false;
            try
            {
                passwordMatches = BCrypt.Net.BCrypt.Verify(seedAdminPassword, admin.PasswordHash);
            }
            catch
            {
                passwordMatches = false;
            }

            if (!passwordMatches)
                admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedAdminPassword);

            admin.IsActive = true;
            db.SaveChanges();
            Console.WriteLine("Applied Program.cs admin username and password to the database.");
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
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

// Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

PredictionApiHost.TryStart(app);

app.Run();