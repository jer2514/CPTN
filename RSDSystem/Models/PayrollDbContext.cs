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
        public DbSet<PayrollSchedule> PayrollSchedules { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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

            modelBuilder.Entity<PayrollSchedule>()
                .HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}