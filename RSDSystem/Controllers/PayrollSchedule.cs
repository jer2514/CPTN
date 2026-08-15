using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class PayrollSchedule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PayrollScheduleId { get; set; }

        [Required]
        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [MaxLength(150)]
        [Display(Name = "Type of Project")]
        public string? TypeOfService { get; set; }

        [Required]
        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime StartingDate { get; set; }

        [Required]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}