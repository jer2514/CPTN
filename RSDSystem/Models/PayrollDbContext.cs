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
        public DbSet<Payroll> Payrolls { get; set; }
        public DbSet<AttendanceImport> AttendanceImports { get; set; }
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Employee>()
                .HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            modelBuilder.Entity<Employee>()
                .HasIndex(e => new { e.FirstName, e.LastName, e.DateOfBirth })
                .IsUnique();

            modelBuilder.Entity<Employee>()
                .Property(e => e.Email)
                .IsRequired(false);

            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.Email)
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL AND [Email] <> ''");

            modelBuilder.Entity<Employee>()
                .Ignore(e => e.FullName)
                .Ignore(e => e.Age);

            modelBuilder.Entity<User>()
                .Ignore(u => u.FullName)
                .Ignore(u => u.Age);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<ProjectMonthlyBudget>()
                .HasOne(m => m.Project)
                .WithMany(p => p.MonthlyBudgets)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
                .Property(p => p.ProjectName)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .Property(p => p.Status)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .Property(p => p.Location)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .Property(p => p.TypeOfService)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .Property(p => p.PayrollDistribution)
                .IsRequired(false);

            modelBuilder.Entity<Project>()
                .Property(p => p.AssignedPayrollStaff)
                .IsRequired(false);

            modelBuilder.Entity<PayrollSchedule>()
                .HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payroll>()
                .HasOne(pr => pr.Employee)
                .WithMany()
                .HasForeignKey(pr => pr.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Payroll>()
                .HasOne(pr => pr.Project)
                .WithMany()
                .HasForeignKey(pr => pr.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AttendanceImport>()
                .HasOne(i => i.Project)
                .WithMany()
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(r => r.Import)
                .WithMany(i => i.Records)
                .HasForeignKey(r => r.AttendanceImportId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(r => r.Employee)
                .WithMany()
                .HasForeignKey(r => r.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
        }
    }
}