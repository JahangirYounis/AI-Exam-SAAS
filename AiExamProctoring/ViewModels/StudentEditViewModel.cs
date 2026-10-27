using AiExamProctoring.Models;
using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.ViewModels
{
    public class StudentDashboardViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        public string? PhoneNumber { get; set; }

        [StringLength(30)]
        public string? RollNumber { get; set; }

        [StringLength(30)]
        public string? RegistrationNumber { get; set; }

        [StringLength(15)]
        public string? CNIC { get; set; }

        [StringLength(100)]
        public string? FatherName { get; set; }

        public string? Gender { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? Department { get; set; }

        public string? Batch { get; set; }

        public string? Semester { get; set; }

        public string? Session { get; set; }

        public string? University { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? Country { get; set; }

        public string? ProfileImage { get; set; }

        public string? HighestQualification { get; set; }

        public EmploymentStatus? CurrentStatus { get; set; }

        public string? OrganizationName { get; set; }

        public bool IsApproved { get; set; }

        public bool IsBlocked { get; set; }
   
    }
}