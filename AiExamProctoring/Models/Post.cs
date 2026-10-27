using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models
{
    public class Post
    {
        [Key]
        public int PostId { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public virtual Department? Department { get; set; }

        [Required]
        [StringLength(150)]
        public string PostName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Grade { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual ICollection<Exam> Exams { get; set; }
            = new List<Exam>();
    }
}