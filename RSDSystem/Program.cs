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
            AttendanceSchema.Ensure(db);
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