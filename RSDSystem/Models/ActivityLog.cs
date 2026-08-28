using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Helpers;

namespace RSDSystem.Models
{
    public class ActivityLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ActivityLogId { get; set; }

        public int? UserId { get; set; }

        [MaxLength(150)]
        public string UserName { get; set; } = "";

        [MaxLength(30)]
        public string Role { get; set; } = "";

        [MaxLength(60)]
        public string Activity { get; set; } = "";

        [MaxLength(40)]
        public string Module { get; set; } = "";

        [MaxLength(500)]
        public string Description { get; set; } = "";

        public int? ProjectId { get; set; }
        public int? RelatedId { get; set; }

        public DateTime CreatedAt { get; set; } = PhilippinesTime.Now;
    }

    public static class ActivityModules
    {
        public const string Authentication = "Authentication";
        public const string Payroll = "Payroll";
        public const string Attendance = "Attendance";
        public const string UserManagement = "User Management";
        public const string Prediction = "Payroll Prediction";
    }

    public static class ActivityTypes
    {
        public const string Login = "Login";
        public const string Logout = "Logout";
        public const string ChangePassword = "Change Password";
        public const string ResetPassword = "Reset Password";
        public const string GeneratePayroll = "Generate Payroll";
        public const string SubmitPayroll = "Submit Payroll";
        public const string ApprovePayroll = "Approve Payroll";
        public const string ReturnPayroll = "Return Payroll";
        public const string SendPayslips = "Send Payslips";
        public const string ImportAttendance = "Import Attendance";
        public const string RequestCorrection = "Request Correction";
        public const string ApproveCorrection = "Approve Correction";
        public const string ReturnCorrection = "Return Correction";
        public const string CreateUser = "Create User";
        public const string EditUser = "Edit User";
        public const string GeneratePrediction = "Load Prediction";
    }
}
