using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required, StringLength(75)]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(75)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(30)]
        public string? RollNumber { get; set; }

        [StringLength(30)]
        public string? RegistrationNumber { get; set; }

        [StringLength(15)]
        public string? CNIC { get; set; }

        [StringLength(100)]
        public string? FatherName { get; set; }

        [StringLength(20)]
        public string? Gender { get; set; }

        public DateTime? DateOfBirth { get; set; }

        // Legacy/display field
        [StringLength(100)]
        public string? Department { get; set; }

        // Master Department relationship
        public int? DepartmentId { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public virtual Department? DepartmentInfo { get; set; }

        [StringLength(50)]
        public string? Batch { get; set; }

        [StringLength(30)]
        public string? Semester { get; set; }

        [StringLength(30)]
        public string? Session { get; set; }

        [StringLength(150)]
        public string? University { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(500)]
        public string? ProfileImage { get; set; }

        public DateTime? ProfilePhotoCapturedAt { get; set; }

        public bool IsApproved { get; set; } = false;

        public bool IsBlocked { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? LastLoginDate { get; set; }

        [StringLength(100)]
        public string? HighestQualification { get; set; }

        public EmploymentStatus? CurrentStatus { get; set; }

        [StringLength(150)]
        public string? OrganizationName { get; set; }

        [StringLength(100)] public string? Province { get; set; }
        [StringLength(100)] public string? District { get; set; }
        [StringLength(100)] public string? Domicile { get; set; }
        [StringLength(100)] public string? DomicileTaluka { get; set; }
        [StringLength(150)] public string? UnionCouncil { get; set; }
        [StringLength(50)] public string? DomicileNumber { get; set; }
        [StringLength(50)] public string? PrcNumber { get; set; }
        public DateTime? DomicileIssueDate { get; set; }
        [StringLength(300)] public string? PermanentAddress { get; set; }
        [StringLength(50)] public string? CandidateNumber { get; set; }
        [StringLength(100)] public string? TestingOrganization { get; set; }

        public virtual ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
        public virtual ICollection<StudentNotification> Notifications { get; set; } = new List<StudentNotification>();
        public virtual ICollection<StudentEducation> Educations { get; set; } = new List<StudentEducation>();
        public virtual ICollection<StudentExperience> Experiences { get; set; } = new List<StudentExperience>();
        public virtual ICollection<StudentCertification> Certifications { get; set; } = new List<StudentCertification>();
    }

    

    public enum EmploymentStatus
    {
        Student,
        Employed,
        SelfEmployed,
        Unemployed
    }
}