using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Models
{
    public class Department
    {
        [Key]
        public int DepartmentId { get; set; }

        [Required]
        [StringLength(150)]
        public string DepartmentName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? DepartmentCode { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual ICollection<Post> Posts { get; set; }
            = new List<Post>();

        public virtual ICollection<Exam> Exams { get; set; }
            = new List<Exam>();

        public virtual ICollection<ApplicationUser> Users { get; set; }
            = new List<ApplicationUser>();
    }
}