/*
 * =============================================================================
 * RSD PAYROLL SYSTEM — HOW THE APP STARTS AND HOW WORK FLOWS
 * =============================================================================
 *
 * This file is the entry point. ASP.NET Core runs it top to bottom:
 *   1) Register services (MVC, SQL Server, session, prediction, notifications)
 *   2) Build the app, migrate/fix the database, seed demo logins
 *   3) Set up the HTTP pipeline (static files → session → auth → MVC routes)
 *   4) Listen (default URL pattern: /Account/Login)
 *
 * TWO ROLES
 *   Admin        — users, employees, projects, review payroll, prediction, reports
 *   PayrollStaff — import attendance, generate slips, submit payroll, to-do tasks
 *
 * MAIN BUSINESS FLOW (read this when tracing a payroll)
 *   Admin creates a Project and assigns a payroll staff member.
 *   Admin adds Employees and assigns them to that project.
 *   Admin adds a PayrollSchedule (pay period dates) on the dashboard.
 *   Staff imports an attendance file for that project/period.
 *   Staff opens Generate Payroll → Load employees → fills the slip → saves Draft.
 *   Staff opens Pending Payroll → Submit. Status becomes Submitted.
 *   Admin reviews on Review Payroll → Approve (locks it) or Return (Correction).
 *   After two finished approved months, Admin can Load Payroll Prediction.
 *
 * REQUEST FLOW FOR EVERY PAGE
 *   Browser → Program.cs pipeline → AuthCheckFilter (are you logged in? right role?)
 *   → Controller action → Service/Helper/DbContext → Razor view or JSON.
 *
 * WHERE TO LOOK NEXT
 *   Filters/AuthCheckFilter.cs     session gate
 *   Controllers/AccountController  login / logout
 *   Models/PayrollDbContext.cs     all SQL tables
 *   Controllers/PayrollStaffController.cs  staff work
 *   Controllers/PayrollController.cs       admin payroll + prediction
 *   Controllers/AttendanceController.cs    import / records / summary
 * =============================================================================
 */

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

// MVC + Razor views. AuthCheckFilter runs before almost every controller action.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<AuthCheckFilter>();
});

// Cookie auth is registered, but login actually stores UserId/Role/FullName in Session
// (see AccountController.SignIn). Logout still calls SignOutAsync to clear the cookie.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "RSDSystemAuth";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Lets Excel/CSV parsers read older Windows encodings (attendance import files).
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Entity Framework → SQL Server. Connection string lives in appsettings.json.
builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// One instance per HTTP request. Controllers receive these through constructors.
builder.Services.AddScoped<AttendanceImportService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHttpClient("PayrollPrediction", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddScoped<PayrollPredictionService>();

// Session holds login: UserId, FullName, Role, PhotoPath (60-minute idle timeout).
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

// Startup DB work: apply EF migrations, patch older columns, then seed demo/payroll logins
// if those usernames are missing. Safe to re-run; it skips users that already exist.
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

// HTTP pipeline order matters: static files first, then session, then MVC routing.
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

// First visit with no URL → AccountController.Login.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

try
{
    Console.WriteLine("On this computer: http://localhost:5114");
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
        Console.WriteLine("Other computers on this Wi-Fi/network: http://" + ip + ":5114");
}
catch
{
    // ignore address lookup failures
}

app.Run();