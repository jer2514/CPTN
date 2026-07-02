using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Models
{
    public class PayrollDbContext : DbContext
    {
        public PayrollDbContext(DbContextOptions<PayrollDbContext> options)
            : base(options)
        {
        }

        public DbSet<Employee> Employees { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectMonthlyBudget> ProjectMonthlyBudgets { get; set; }
         
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // FullName and Age are computed properties — not stored in DB  
            modelBuilder.Entity<Employee>()
                .HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .Ignore(u => u.FullName)
                .Ignore(u => u.Age);

            modelBuilder.Entity<ProjectMonthlyBudget>()
            .HasOne(m => m.Project)
            .WithMany(p => p.MonthlyBudgets)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);



        }
    }
}