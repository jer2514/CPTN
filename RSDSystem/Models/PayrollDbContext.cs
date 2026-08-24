using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Models
{
    /// <summary>
    /// Entity Framework map of the SQL Server database.
    /// Each DbSet is a table. Controllers query these instead of writing SQL
    /// (except Program.cs / *Schema.Ensure which patch older databases).
    ///
    /// OnModelCreating sets relationships and unique indexes:
    /// employees belong to a project (optional), payroll belongs to employee+project,
    /// one slip per employee per payroll schedule, attendance rows belong to an import batch.
    /// </summary>
    public class PayrollDbContext : DbContext
    {
        /// <summary>Passes SQL Server options from Program.cs (connection string) into EF Core.</summary>
        public PayrollDbContext(DbContextOptions<PayrollDbContext> options)
            : base(options)
        {
        }

        /// <summary>Field workers; EmployeeController CRUD and payroll/attendance lookups.</summary>
        public DbSet<Employee> Employees { get; set; }

        /// <summary>Admin and PayrollStaff login accounts; Account/UserManagement.</summary>
        public DbSet<User> Users { get; set; }

        /// <summary>Construction jobs; ProjectController and staff assignment.</summary>
        public DbSet<Project> Projects { get; set; }

        /// <summary>Optional per-month budget caps used by payroll prediction.</summary>
        public DbSet<ProjectMonthlyBudget> ProjectMonthlyBudgets { get; set; }

        /// <summary>Admin-created pay periods; become staff to-do tasks.</summary>
        public DbSet<PayrollSchedule> PayrollSchedules { get; set; }

        /// <summary>Payslip rows (Draft → Submitted → Approved / Correction).</summary>
        public DbSet<Payroll> Payrolls { get; set; }

        /// <summary>One Excel/CSV upload batch per project; parent of AttendanceRecords.</summary>
        public DbSet<AttendanceImport> AttendanceImports { get; set; }

        /// <summary>One person's punches for one calendar day after import.</summary>
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }

        /// <summary>Bell items for Admin (all) or one PayrollStaff by name.</summary>
        public DbSet<AppNotification> AppNotifications { get; set; }

        /// <summary>Staff punch-change requests waiting for Admin approve/return.</summary>
        public DbSet<AttendanceCorrectionRequest> AttendanceCorrectionRequests { get; set; }
        public DbSet<ProjectEmployeeHistory> ProjectEmployeeHistories { get; set; }
        public DbSet<PayrollPredictionHistory> PayrollPredictionHistories { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }

        /// <summary>Defines FKs, unique indexes, optional columns, and ignored computed properties.</summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Employee → optional Project; deleting a project unassigns workers (SetNull).
            modelBuilder.Entity<Employee>()
                .HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            // Same person (name + DOB) cannot be added twice.
            modelBuilder.Entity<Employee>()
                .HasIndex(e => new { e.FirstName, e.LastName, e.DateOfBirth })
                .IsUnique();

            modelBuilder.Entity<Employee>()
                .Property(e => e.Email)
                .IsRequired(false);

            // Email unique only when it is actually filled in.
            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.Email)
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL AND [Email] <> ''");

            // FullName and Age are C# getters, not columns.
            modelBuilder.Entity<Employee>()
                .Ignore(e => e.FullName)
                .Ignore(e => e.Age);

            // User computed properties + unique login/email.
            modelBuilder.Entity<User>()
                .Ignore(u => u.FullName)
                .Ignore(u => u.Age);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // ProjectMonthlyBudget → Project (cascade: delete budgets with the job).
            modelBuilder.Entity<ProjectMonthlyBudget>()
                .HasOne(m => m.Project)
                .WithMany(p => p.MonthlyBudgets)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Project string columns are optional at the database (forms still require them).
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

            // PayrollSchedule → Project (cascade: delete schedules with the job).
            modelBuilder.Entity<PayrollSchedule>()
                .HasOne(s => s.Project)
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payroll → Employee and Project (Restrict so you cannot delete a worker/job that has slips).
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

            // Payroll → optional PayrollSchedule (SetNull if the schedule is deleted).
            modelBuilder.Entity<Payroll>()
                .HasOne(pr => pr.PayrollSchedule)
                .WithMany()
                .HasForeignKey(pr => pr.PayrollScheduleId)
                .OnDelete(DeleteBehavior.SetNull);

            // One slip per employee per schedule (when a schedule id is present).
            modelBuilder.Entity<Payroll>()
                .HasIndex(pr => new { pr.EmployeeId, pr.PayrollScheduleId })
                .IsUnique()
                .HasFilter("[PayrollScheduleId] IS NOT NULL");

            // AttendanceImport → Project (cascade: delete the upload batch with the job).
            modelBuilder.Entity<AttendanceImport>()
                .HasOne(i => i.Project)
                .WithMany()
                .HasForeignKey(i => i.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // AttendanceRecord → Import (cascade: delete daily rows with the batch).
            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(r => r.Import)
                .WithMany(i => i.Records)
                .HasForeignKey(r => r.AttendanceImportId)
                .OnDelete(DeleteBehavior.Cascade);

            // AttendanceRecord → optional Employee (NoAction so deleting a worker does not wipe punches).
            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(r => r.Employee)
                .WithMany()
                .HasForeignKey(r => r.EmployeeId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // Bell list lookup: role + recipient name + newest first.
            modelBuilder.Entity<AppNotification>()
                .HasIndex(n => new { n.RecipientRole, n.RecipientName, n.CreatedAt });

            // Correction request → the punch row (cascade) and the project (NoAction).
            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(c => c.Record)
                .WithMany()
                .HasForeignKey(c => c.AttendanceRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AttendanceCorrectionRequest>()
                .HasOne(c => c.Project)
                .WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ProjectEmployeeHistory>()
                .HasOne(h => h.Project)
                .WithMany()
                .HasForeignKey(h => h.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProjectEmployeeHistory>()
                .HasOne(h => h.Employee)
                .WithMany()
                .HasForeignKey(h => h.EmployeeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ProjectEmployeeHistory>()
                .HasIndex(h => new { h.ProjectId, h.EmployeeId })
                .IsUnique();

            modelBuilder.Entity<PayrollPredictionHistory>()
                .HasOne(h => h.Project)
                .WithMany()
                .HasForeignKey(h => h.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayrollPredictionHistory>()
                .HasIndex(h => new { h.ProjectId, h.GeneratedAt });

            modelBuilder.Entity<ActivityLog>()
                .HasIndex(a => a.CreatedAt);
        }
    }
}
