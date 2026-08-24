using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    /// <summary>
    /// Snapshot of who was employed on a project so a finished project still
    /// shows its roster after employees are unassigned and marked inactive.
    /// </summary>
    public class ProjectEmployeeHistory
    {
        [Key]
        public int Id { get; set; }

        public int ProjectId { get; set; }

        [ForeignKey(nameof(ProjectId))]
        public Project? Project { get; set; }

        public int EmployeeId { get; set; }

        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.Now;
    }
}
