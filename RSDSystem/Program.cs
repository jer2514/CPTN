using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RSDSystem.Filters;
using RSDSystem.Models;
using System;

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

builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

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