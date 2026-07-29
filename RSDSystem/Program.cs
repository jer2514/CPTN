using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using RSDSystem.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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

Console.WriteLine("== DEBUG: Using connection string = " + builder.Configuration.GetConnectionString("DefaultConnection"));

var app = builder.Build();

// Seed demo user so you can login from any device
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<PayrollDbContext>();

        // Optional: apply migrations automatically in development - remove for production deployments
        try
        {
            db.Database.Migrate();
        }
        catch
        {
            // ignore migration errors in local dev if DB unavailable
        }

        if (!db.Users.Any(u => u.Username == "demo"))
        {
            var demoUser = new User
            {
                FirstName = "Demo",
                LastName = "User",
                Username = "demo",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Email = "demo@example.com",
                ContactNumber = "0000000000",
                Address = "Demo account",
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            db.Users.Add(demoUser);
            db.SaveChanges();
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

// Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();