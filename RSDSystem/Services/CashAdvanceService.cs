using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Services
{
    public class CashAdvanceService
    {
        private readonly PayrollDbContext _db;

        public CashAdvanceService(PayrollDbContext db)
        {
            _db = db;
        }

        public async Task<CashAdvanceTotals> ProjectTotalsAsync(int projectId, CancellationToken cancellationToken = default)
        {
            var rows = await _db.CashAdvances.AsNoTracking()
                .Where(c => c.ProjectId == projectId)
                .Select(c => new { c.Amount, c.Status })
                .ToListAsync(cancellationToken);

            return new CashAdvanceTotals
            {
                Total = rows.Sum(r => r.Amount),
                Unpaid = rows.Where(r => CashAdvanceStatuses.IsUnpaid(r.Status)).Sum(r => r.Amount),
                Paid = rows.Where(r => CashAdvanceStatuses.IsPaid(r.Status)).Sum(r => r.Amount)
            };
        }

        public async Task<List<CashAdvanceEmployeeRow>> EmployeeRowsAsync(
            int projectId, string? search, string? status, CancellationToken cancellationToken = default)
        {
            var employees = await _db.Employees.AsNoTracking()
                .Where(e => e.ProjectId == projectId && e.IsActive)
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync(cancellationToken);

            var advances = await _db.CashAdvances.AsNoTracking()
                .Where(c => c.ProjectId == projectId)
                .Select(c => new { c.EmployeeId, c.Amount, c.Status })
                .ToListAsync(cancellationToken);

            var q = (search ?? "").Trim();
            var filter = (status ?? "all").Trim().ToLowerInvariant();

            var rows = new List<CashAdvanceEmployeeRow>();
            foreach (var emp in employees)
            {
                var name = emp.FullName;
                if (!string.IsNullOrEmpty(q)
                    && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && (emp.EmployeeCode ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && (emp.JobClassification ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var empRows = advances.Where(a => a.EmployeeId == emp.EmployeeId).ToList();
                var unpaid = empRows.Where(a => CashAdvanceStatuses.IsUnpaid(a.Status)).Sum(a => a.Amount);
                var outstanding = empRows.Where(a => a.Status == CashAdvanceStatuses.Outstanding).Sum(a => a.Amount);
                var paid = empRows.Where(a => CashAdvanceStatuses.IsPaid(a.Status)).Sum(a => a.Amount);
                if (filter == "unpaid" && unpaid <= 0)
                    continue;
                if (filter == "paid" && paid <= 0)
                    continue;

                rows.Add(new CashAdvanceEmployeeRow
                {
                    EmployeeId = emp.EmployeeId,
                    DisplayId = EmployeeIds.Format(emp.EmployeeCode),
                    EmployeeName = name,
                    Job = emp.JobClassification ?? "—",
                    Total = empRows.Sum(a => a.Amount),
                    Unpaid = unpaid,
                    Outstanding = outstanding,
                    Paid = paid
                });
            }

            return rows;
        }

        public async Task<List<CashAdvancePendingRow>> PendingByEmployeeAsync(
            int projectId, CancellationToken cancellationToken = default)
        {
            var rows = await _db.CashAdvances.AsNoTracking()
                .Include(c => c.Employee)
                .Where(c => c.ProjectId == projectId && c.Status == CashAdvanceStatuses.Pending)
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(c => c.EmployeeId)
                .Select(g =>
                {
                    var emp = g.First().Employee;
                    return new CashAdvancePendingRow
                    {
                        EmployeeId = g.Key,
                        EmployeeName = emp?.FullName ?? "Employee",
                        Job = emp?.JobClassification ?? "—",
                        Amount = g.Sum(x => x.Amount),
                        Count = g.Count()
                    };
                })
                .OrderBy(r => r.EmployeeName)
                .ToList();
        }

        public async Task<decimal> PendingAmountAsync(int projectId, int employeeId, CancellationToken cancellationToken = default)
        {
            return await _db.CashAdvances.AsNoTracking()
                .Where(c => c.ProjectId == projectId
                    && c.EmployeeId == employeeId
                    && c.Status == CashAdvanceStatuses.Pending)
                .SumAsync(c => (decimal?)c.Amount, cancellationToken) ?? 0;
        }

        public async Task<decimal> AvailablePendingAmountAsync(
            int projectId, int employeeId, int? excludePayrollId = null,
            CancellationToken cancellationToken = default)
        {
            var pending = await PendingAmountAsync(projectId, employeeId, cancellationToken);
            if (pending <= 0)
                return 0;

            var othersQuery = _db.Set<Payroll>().AsNoTracking()
                .Where(p => p.ProjectId == projectId && p.EmployeeId == employeeId);
            if (excludePayrollId is int id && id > 0)
                othersQuery = othersQuery.Where(p => p.PayrollId != id);

            var others = await othersQuery
                .Select(p => new { p.Status, p.CashAdvance })
                .ToListAsync(cancellationToken);

            return CashAdvanceReservation.AvailableForPayroll(
                pending, others.Select(p => (p.Status ?? "", p.CashAdvance)));
        }

        public async Task<(string? Error, CashAdvance? Entry)> AddAsync(
            int projectId, int employeeId, DateTime? advanceDate, decimal amount, string? reason, string createdBy,
            CancellationToken cancellationToken = default)
        {
            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
            if (project == null)
                return ("Project not found.", null);
            if (ProjectStatusOptions.IsFinished(project.Status))
                return ("Finished projects cannot receive new cash advances.", null);

            var employee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.ProjectId == projectId, cancellationToken);
            if (employee == null)
                return ("Employee is not assigned to this project.", null);

            if (!DateRules.IsUsableDate(advanceDate))
                return (DateRules.CalendarYearMessage, null);
            if (amount <= 0)
                return ("Enter an amount greater than 0.", null);
            if (amount > 9999999.99m)
                return ("Amount is too large.", null);

            var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (note != null && note.Length > 400)
                return ("Reason must be 400 characters or less.", null);

            var entry = new CashAdvance
            {
                ProjectId = projectId,
                EmployeeId = employeeId,
                AdvanceDate = advanceDate!.Value.Date,
                Amount = decimal.Round(amount, 2),
                Reason = note,
                Status = CashAdvanceStatuses.Outstanding,
                CreatedAt = PhilippinesTime.Now,
                CreatedBy = createdBy
            };
            _db.CashAdvances.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
            return (null, entry);
        }

        public async Task<(string? Error, CashAdvance? Entry)> MarkOneAsync(
            int cashAdvanceId, string markedBy, CancellationToken cancellationToken = default)
        {
            var entry = await _db.CashAdvances
                .Include(c => c.Employee)
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.CashAdvanceId == cashAdvanceId, cancellationToken);
            if (entry == null)
                return ("Cash advance not found.", null);
            if (ProjectStatusOptions.IsFinished(entry.Project?.Status))
                return ("Finished projects cannot change cash advances.", null);
            if (entry.Status != CashAdvanceStatuses.Outstanding)
                return ("Only unpaid cash advances can be marked for the next payroll.", null);

            entry.Status = CashAdvanceStatuses.Pending;
            entry.MarkedAt = PhilippinesTime.Now;
            entry.MarkedBy = markedBy;
            await _db.SaveChangesAsync(cancellationToken);
            return (null, entry);
        }

        public async Task<(string? Error, decimal Amount, int Count, string EmployeeName)> MarkOutstandingAsync(
            int projectId, int employeeId, string markedBy, CancellationToken cancellationToken = default)
        {
            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
            if (project == null)
                return ("Project not found.", 0, 0, "");
            if (ProjectStatusOptions.IsFinished(project.Status))
                return ("Finished projects cannot change cash advances.", 0, 0, "");

            var employee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);
            var name = employee?.FullName ?? "the employee";

            var rows = await _db.CashAdvances
                .Where(c => c.ProjectId == projectId
                    && c.EmployeeId == employeeId
                    && c.Status == CashAdvanceStatuses.Outstanding)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return ("This employee has no unpaid cash advance to deduct.", 0, 0, name);

            var now = PhilippinesTime.Now;
            foreach (var row in rows)
            {
                row.Status = CashAdvanceStatuses.Pending;
                row.MarkedAt = now;
                row.MarkedBy = markedBy;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return (null, rows.Sum(r => r.Amount), rows.Count, name);
        }

        public async Task ApplyToApprovedPayrollAsync(Payroll payroll, CancellationToken cancellationToken = default)
        {
            var pending = await _db.CashAdvances
                .Where(c => c.ProjectId == payroll.ProjectId
                    && c.EmployeeId == payroll.EmployeeId
                    && c.Status == CashAdvanceStatuses.Pending)
                .OrderBy(c => c.AdvanceDate)
                .ThenBy(c => c.CashAdvanceId)
                .ToListAsync(cancellationToken);
            if (pending.Count == 0)
                return;

            var remaining = payroll.CashAdvance;
            var now = PhilippinesTime.Now;
            foreach (var row in pending)
            {
                if (remaining <= 0)
                    break;
                if (row.Amount > remaining)
                    continue;
                row.Status = CashAdvanceStatuses.Deducted;
                row.PayrollId = payroll.PayrollId;
                row.DeductedAt = now;
                remaining -= row.Amount;
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
