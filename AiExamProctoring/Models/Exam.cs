using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models;

public enum ExamStatus
{
    Draft,
    Building,
    Ready,
    Scheduled,
    Running,
    Completed,
    Cancelled
}

public enum ExamAudience
{
    [Display(Name = "Private — selected class/candidates")]
    Private,

    [Display(Name = "Public — open to every student")]
    Public
}

public enum ExamPurpose
{
    [Display(Name = "Academic — School, College, University or Academy")]
    AcademicClass,

    [Display(Name = "Recruitment / Job Test")]
    Recruitment,

    [Display(Name = "Practice / Mock Test")]
    Practice,

    [Display(Name = "Certification Test")]
    Certification,

    [Display(Name = "Other / Custom Test")]
    Other,

    [Display(Name = "University Entry Test")]
    Entrance
}

public enum ExamBlockingMode { BOTH = 0, AUTO_ONLY = 1, MANUAL_ONLY = 2 }

public class Exam
{
    public DateTime? CreatorFirstMonitoringAtUtc { get; set; }
    public DateTime? CreatorLastMonitoringAtUtc { get; set; }
    [Range(1, 1000)] public int? QuestionLimit { get; set; }
    [StringLength(150)] public string? RecruitmentDepartment { get; set; }
    [StringLength(150)] public string? RecruitmentPost { get; set; }
    [StringLength(30)] public string? RecruitmentGrade { get; set; }
    public int? AcademicClassId { get; set; }
    public AcademicClass? AcademicClass { get; set; }
    [EnumDataType(typeof(AutomaticViolationAction))]
    public AutomaticViolationAction ViolationAction { get; set; } = AutomaticViolationAction.LockForReview;

    [EnumDataType(typeof(ExamBlockingMode))]
    public ExamBlockingMode BlockingMode { get; set; } = ExamBlockingMode.BOTH;
    [Key]
    public int ExamId { get; set; }


    // ============================================================
    // BASIC INFORMATION
    // ============================================================

    [Required]
    [StringLength(200)]
    [Display(Name = "Examination Title")]
    public string ExamTitle { get; set; } = string.Empty;


    [StringLength(2000)]
    [Display(Name = "Examination Description")]
    public string? Description { get; set; }


    [StringLength(2000)]
    [Display(Name = "Candidate Instructions")]
    public string? Instructions { get; set; }

    [Display(Name = "Exam Access")]
    public ExamAudience Audience { get; set; } = ExamAudience.Private;

    [Display(Name = "Exam Purpose")]
    public ExamPurpose Purpose { get; set; } = ExamPurpose.Entrance;

    [StringLength(150)]
    [Display(Name = "Class / Recruitment Group")]
    public string? ClassName { get; set; }

    [StringLength(50)]
    [Display(Name = "Academic Level")]
    public string? AcademicLevel { get; set; }

    [StringLength(40)]
    [Display(Name = "Private Class Code")]
    public string? ClassCode { get; set; }

    [Display(Name = "Reusable Public Practice Exam")]
    public bool IsReusable { get; set; }

    [NotMapped]
    public bool AllowsRepeatAttempts => IsReusable && Audience == ExamAudience.Public && Purpose == ExamPurpose.Practice;

    [Range(1, 100)]
    [Display(Name = "Maximum Attempts per Student")]
    public int MaximumAttempts { get; set; } = 1;


    // ============================================================
    // OPTIONAL TARGETING
    // ============================================================

    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    public virtual Department? Department { get; set; }


    [Display(Name = "Post / Position")]
    public int? PostId { get; set; }

    public virtual Post? Post { get; set; }


    // ============================================================
    // AUTOMATIC EXAM TOTALS
    // ============================================================
    //
    // These values are maintained by ExamQuestion selection.
    //
    // They are NOT entered manually as a fixed question limit.
    //

    [Display(Name = "Selected Questions")]
    public int TotalQuestions { get; set; }


    [Column(TypeName = "decimal(10,2)")]
    [Range(typeof(decimal), "0.01", "100000")]
    [Display(Name = "Total Marks")]
    public decimal TotalMarks { get; set; } = 100m;

    [Column(TypeName = "decimal(6,2)")]
    [Range(typeof(decimal), "0", "100")]
    [Display(Name = "Negative Marks per Wrong Answer")]
    public decimal NegativeMarksPerWrongAnswer { get; set; }


    // ============================================================
    // PASSING
    // ============================================================

    [Range(0, 100)]
    [Display(Name = "Passing Percentage")]
    public decimal PassingPercentage { get; set; } = 40m;


    // ============================================================
    // TIME / SESSIONS
    // ============================================================

    [Range(1, 1440)]
    [Display(Name = "Total Duration (Minutes)")]
    public int DurationMinutes { get; set; } = 100;


    [Range(1, 20)]
    [Display(Name = "Number of Sessions")]
    public int NumberOfSessions { get; set; } = 1;


    [Display(Name = "Exam Date")]
    [DataType(DataType.Date)]
    public DateTime ExamDate { get; set; } = DateTime.Today;


    [Display(Name = "Start Time")]
    public TimeSpan StartTime { get; set; } =
        new(10, 0, 0);


    [Range(0, 1440)]
    [Display(Name = "Late Entry Window (Minutes)")]
    public int EntryWindowMinutes { get; set; } = 30;


    // ============================================================
    // EXAM BEHAVIOUR
    // ============================================================

    [Display(Name = "Unique Question Order per Candidate")]
    public bool ShuffleQuestions { get; set; } = true;

    [Display(Name = "Shuffle Answer Options")]
    public bool ShuffleOptions { get; set; } = true;

    [Display(Name = "Allow Back Navigation")]
    public bool AllowBackNavigation { get; set; } = true;

    [Display(Name = "Auto-submit When Time Ends")]
    public bool AutoSubmitOnTimeEnd { get; set; } = true;


    // ============================================================
    // SECURITY / PROCTORING
    // ============================================================

    [Display(Name = "Require Fullscreen Mode")]
    public bool RequireFullscreen { get; set; } = true;

    [Display(Name = "Require Camera")]
    public bool RequireCamera { get; set; } = true;

    [Display(Name = "Require Microphone")]
    public bool RequireMicrophone { get; set; } = true;


    // ============================================================
    // STATUS
    // ============================================================

    public bool AdmissionsOpen { get; set; }
    [Range(typeof(decimal), "0", "1000000")] public decimal ApplicationFee { get; set; }
    public DateTime? ApplicationDeadline { get; set; }
    public DateTime? ResultsPublishedAtUtc { get; set; }
    [StringLength(450)] public string? ResultsPublishedBy { get; set; }

    public bool IsPublished { get; set; }

    public bool IsLocked { get; set; }

    public ExamStatus Status { get; set; } =
        ExamStatus.Draft;


    // ============================================================
    // AUDIT
    // ============================================================

    public DateTime CreatedDate { get; set; } =
        DateTime.Now;


    public DateTime? UpdatedDate { get; set; }


    [StringLength(450)]
    public string? CreatedBy { get; set; }


    [StringLength(450)]
    public string? UpdatedBy { get; set; }


    // ============================================================
    // RELATIONSHIPS
    // ============================================================

    public virtual ICollection<ExamSubjectPart> SubjectParts { get; set; }
        = new List<ExamSubjectPart>();


    public virtual ICollection<ExamQuestion> ExamQuestions { get; set; }
        = new List<ExamQuestion>();


    public virtual ICollection<ExamSession> Sessions { get; set; }
        = new List<ExamSession>();


    public virtual ICollection<ExamAttempt> Attempts { get; set; }
        = new List<ExamAttempt>();

    public virtual ICollection<ExamAssignment> Assignments { get; set; }
        = new List<ExamAssignment>();

    public virtual ICollection<StudentNotification> Notifications { get; set; }
        = new List<StudentNotification>();


    // ============================================================
    // CALCULATED HELPERS
    // ============================================================

    [NotMapped]
    public DateTime StartDate =>
        ExamDate.Date.Add(StartTime);


    [NotMapped]
    public DateTime LatestStartDate =>
        IsReusable ? DateTime.MaxValue : StartDate.AddMinutes(Math.Max(0, EntryWindowMinutes));

    // Nominal scheduled end for display/reporting only. Once a candidate starts,
    // every session is timed independently from its server-side StartedAt value and
    // permitted rest breaks are not deducted from session time.
    [NotMapped]
    public DateTime EndDate =>
        IsReusable
            ? DateTime.MaxValue
            : LatestStartDate.AddMinutes(DurationMinutes + Math.Max(0, NumberOfSessions - 1) * 10);




    [NotMapped]
    public decimal MarksPerQuestion =>
        TotalQuestions <= 0 ? 0m : Math.Round(TotalMarks / TotalQuestions, 4);

    [NotMapped]
    public decimal PassingMarks =>
        Math.Round(
            TotalMarks * PassingPercentage / 100m,
            2);


    [NotMapped]
    public bool IsActive =>
        IsPublished &&
        Status != ExamStatus.Cancelled &&
        Status != ExamStatus.Completed;


    [NotMapped]
    public string Title =>
        ExamTitle;


    [NotMapped]
    public string Category =>
        RecruitmentPost ?? Post?.PostName ??
        Department?.DepartmentName ??
        ClassName ??
        AcademicLevel ??
        "General";

    [NotMapped]
    public string AudienceDisplay => Audience == ExamAudience.Public
        ? "Public Exam"
        : "Private Class Exam";

    [NotMapped]
    public string PurposeDisplay => Purpose switch
    {
        ExamPurpose.Entrance => "University Entry Test",
        ExamPurpose.AcademicClass => "Class / Academic Test",
        ExamPurpose.Recruitment => "Job / Recruitment Test",
        ExamPurpose.Certification => "Certification Test",
        ExamPurpose.Other => "Other / Custom Test",
        _ => "Practice / Mock Test"
    };


    [NotMapped]
    public int SessionCount =>
        Sessions?.Count(x => x.IsActive) ?? 0;


    [NotMapped]
    public int ActiveQuestionCount =>
        ExamQuestions?.Count(x => x.IsActive) ?? 0;


    [NotMapped]
    public bool IsReadyForScheduling =>
        TotalQuestions > 0 &&
        TotalMarks > 0 &&
        ActiveQuestionCount == TotalQuestions &&
        Sessions != null &&
        Sessions.Count(x => x.IsActive) == NumberOfSessions &&
        Sessions
            .Where(x => x.IsActive)
            .Sum(x => x.TotalQuestions)
            == TotalQuestions &&
        Sessions
            .Where(x => x.IsActive)
            .Sum(x => x.DurationMinutes)
            == DurationMinutes;
}
