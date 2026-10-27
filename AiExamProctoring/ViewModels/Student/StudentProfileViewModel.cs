using AiExamProctoring.Models;
using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.ViewModels.Student;

public class StudentProfileViewModel
{
    public string Id { get; set; } = string.Empty;
    [Required, StringLength(150)] public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    [Phone] public string? PhoneNumber { get; set; }
    [StringLength(15)] public string? CNIC { get; set; }
    [StringLength(100)] public string? FatherName { get; set; }
    public string? Gender { get; set; }
    [DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
    public string? RollNumber { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Department { get; set; }
    public string? Batch { get; set; }
    public string? Semester { get; set; }
    public string? Session { get; set; }
    public string? University { get; set; }
    public string? HighestQualification { get; set; }
    public EmploymentStatus? CurrentStatus { get; set; }
    public string? OrganizationName { get; set; }
    public string? Province { get; set; }
    public string? District { get; set; }
    public string? Domicile { get; set; }
    public string? DomicileTaluka { get; set; }
    public string? UnionCouncil { get; set; }
    public string? DomicileNumber { get; set; }
    public string? PrcNumber { get; set; }
    [DataType(DataType.Date)] public DateTime? DomicileIssueDate { get; set; }
    public string? PermanentAddress { get; set; }
    public string? CandidateNumber { get; set; }
    public string? TestingOrganization { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? ProfileImage { get; set; }
    [Display(Name="Upload New Photo")] public IFormFile? ImageFile { get; set; }
    public bool IsApproved { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public int TotalExams { get; set; }
    public int CompletedExams { get; set; }
    public int OngoingExams { get; set; }
    public int PassedExams { get; set; }
    public int FailedExams { get; set; }
    public double AverageMarks { get; set; }
    public double PassPercentage { get; set; }
    public int ProfileCompletion { get; set; }
    public List<StudentEducation> Educations { get; set; } = new();
    public List<StudentExperience> Experiences { get; set; } = new();
    public List<StudentCertification> Certifications { get; set; } = new();
}
