using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.ViewModels;

public sealed class TeacherProfileViewModel
{
    [Required, StringLength(75)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(75)] public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Email { get; set; } = string.Empty;
    [Phone] public string? PhoneNumber { get; set; }
    [StringLength(15)] public string? CNIC { get; set; }
    [StringLength(100)] public string? HighestQualification { get; set; }
    [StringLength(100)] public string? Department { get; set; }
    [StringLength(150)] public string? OrganizationName { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(250)] public string? Address { get; set; }
    public string? ProfileImage { get; set; }
    public DateTime? ProfilePhotoCapturedAt { get; set; }
    public IFormFile? ImageFile { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public int ExaminationCount { get; set; }
    public int QuestionCount { get; set; }
    public int CandidateCount { get; set; }
}
