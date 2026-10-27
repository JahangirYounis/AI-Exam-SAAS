using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<TeacherConnection> TeacherConnections => Set<TeacherConnection>();
    public DbSet<TeacherPrivateMessage> TeacherPrivateMessages => Set<TeacherPrivateMessage>();
    public DbSet<ExamTeamMessage> ExamTeamMessages => Set<ExamTeamMessage>();
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // ============================================================
    // IDENTITY / USERS
    // ============================================================

    public DbSet<ApplicationUser> ApplicationUsers =>
        Set<ApplicationUser>();


    // ============================================================
    // MASTER DATA
    // ============================================================

    public DbSet<Department> Departments =>
        Set<Department>();

    public DbSet<Post> Posts =>
        Set<Post>();


    // ============================================================
    // QUESTION BANK
    // ============================================================

    public DbSet<Subject> Subjects =>
        Set<Subject>();

    public DbSet<Question> Questions =>
        Set<Question>();


    // ============================================================
    // EXAM CONFIGURATION
    // ============================================================

    public DbSet<Exam> Exams =>
        Set<Exam>();

    public DbSet<ExamSubjectPart> ExamSubjectParts =>
        Set<ExamSubjectPart>();

    public DbSet<ExamQuestion> ExamQuestions =>
        Set<ExamQuestion>();

    public DbSet<ExamAssignment> ExamAssignments =>
        Set<ExamAssignment>();

    public DbSet<StudentNotification> StudentNotifications =>
        Set<StudentNotification>();

    public DbSet<StudentEducation> StudentEducations => Set<StudentEducation>();
    public DbSet<StudentExperience> StudentExperiences => Set<StudentExperience>();
    public DbSet<StudentCertification> StudentCertifications => Set<StudentCertification>();


    // ============================================================
    // EXAM SESSIONS
    // ============================================================

    public DbSet<ExamSession> ExamSessions =>
        Set<ExamSession>();

    public DbSet<ExamSessionSubject> ExamSessionSubjects =>
        Set<ExamSessionSubject>();


    // ============================================================
    // STUDENT ATTEMPTS
    // ============================================================

    public DbSet<ExamAttempt> ExamAttempts =>
        Set<ExamAttempt>();

    public DbSet<ExamSessionAttempt> ExamSessionAttempts =>
        Set<ExamSessionAttempt>();

    public DbSet<StudentAnswer> StudentAnswers =>
        Set<StudentAnswer>();


    // ============================================================
    // RESULTS
    // ============================================================

    public DbSet<ExamResult> ExamResults =>
        Set<ExamResult>();


    // ============================================================
    // PROCTORING
    // ============================================================

    public DbSet<ProctoringLog> ProctoringLogs =>
        Set<ProctoringLog>();


    // ============================================================
    // MODEL CONFIGURATION
    // ============================================================

    public DbSet<UniversityCandidate> UniversityCandidates => Set<UniversityCandidate>();
    public DbSet<AdmissionApplication> AdmissionApplications => Set<AdmissionApplication>();
    public DbSet<ExamInvigilator> ExamInvigilators => Set<ExamInvigilator>();
    public DbSet<AdmissionQuestionDecision> AdmissionQuestionDecisions => Set<AdmissionQuestionDecision>();
    public DbSet<AdmissionAudit> AdmissionAudits => Set<AdmissionAudit>();

    public DbSet<AcademicClass> AcademicClasses => Set<AcademicClass>();
    public DbSet<AttemptIdentityPhoto> AttemptIdentityPhotos => Set<AttemptIdentityPhoto>();
    public DbSet<ClassMembership> ClassMemberships => Set<ClassMembership>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ProctoringLog>().HasIndex(l => new { l.PolicyPending, l.PolicyProcessed })
            .HasFilter("[PolicyPending] = 1 AND [PolicyProcessed] = 0");
        base.OnModelCreating(builder);
        builder.Entity<ExamInvigilator>().Property(i => i.CanEditQuestions).HasDefaultValue(true).HasSentinel(true);
        builder.Entity<ExamInvigilator>().Property(i => i.CanMonitor).HasDefaultValue(true).HasSentinel(true);
        builder.Entity<ClassMembership>().HasIndex(x => new { x.AcademicClassId, x.StudentId }).IsUnique();
        builder.Entity<ClassMembership>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AcademicClass>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Exam>().HasOne(x => x.AcademicClass).WithMany().HasForeignKey(x => x.AcademicClassId).OnDelete(DeleteBehavior.Restrict);
        AdmissionsModelConfiguration.Configure(builder);
        // Match the persisted Identity schema in runtime, tests and design-time
        // contexts, including when IdentityOptions are not registered by a host.
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().Property(x => x.ProviderKey).HasMaxLength(128);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().Property(x => x.Name).HasMaxLength(128);


        // ========================================================
        // APPLICATION USER
        // ========================================================

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.FirstName).HasMaxLength(75).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(75).IsRequired();

            entity.Property(x => x.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.RollNumber)
                .HasMaxLength(30);

            entity.Property(x => x.RegistrationNumber)
                .HasMaxLength(30);

            entity.Property(x => x.CNIC)
                .HasMaxLength(15);

            entity.Property(x => x.FatherName)
                .HasMaxLength(100);

            entity.Property(x => x.Gender)
                .HasMaxLength(20);

            entity.Property(x => x.Department)
                .HasMaxLength(100);

            entity.Property(x => x.Batch)
                .HasMaxLength(50);

            entity.Property(x => x.Semester)
                .HasMaxLength(30);

            entity.Property(x => x.Session)
                .HasMaxLength(30);

            entity.Property(x => x.University)
                .HasMaxLength(150);

            entity.Property(x => x.Address)
                .HasMaxLength(250);

            entity.Property(x => x.City)
                .HasMaxLength(100);

            entity.Property(x => x.Country)
                .HasMaxLength(100);

            entity.Property(x => x.ProfileImage)
                .HasMaxLength(500);

            entity.Property(x => x.HighestQualification)
                .HasMaxLength(100);

            entity.Property(x => x.OrganizationName)
                .HasMaxLength(150);

            entity.Property(x => x.Province)
                .HasMaxLength(100);

            entity.Property(x => x.District)
                .HasMaxLength(100);

            entity.Property(x => x.Domicile)
                .HasMaxLength(100);

            entity.Property(x => x.DomicileTaluka).HasMaxLength(100);
            entity.Property(x => x.UnionCouncil).HasMaxLength(150);
            entity.Property(x => x.DomicileNumber).HasMaxLength(50);
            entity.Property(x => x.PrcNumber).HasMaxLength(50);
            entity.Property(x => x.PermanentAddress).HasMaxLength(300);

            entity.Property(x => x.CandidateNumber)
                .HasMaxLength(50);

            entity.Property(x => x.TestingOrganization)
                .HasMaxLength(100);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            // Student ? Department
            // Student deletion must NOT delete Department.

            entity.HasOne(x => x.DepartmentInfo)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });


        // ========================================================
        // DEPARTMENT
        // ========================================================

        builder.Entity<Department>(entity =>
        {
            entity.HasKey(x => x.DepartmentId);

            entity.Property(x => x.DepartmentName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.DepartmentCode)
                .HasMaxLength(50);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            entity.HasIndex(x => x.DepartmentName)
                .IsUnique();


            // Department ? Posts
            entity.HasMany(x => x.Posts)
                .WithOne(x => x.Department)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);


            // Department ? Exams
            entity.HasMany(x => x.Exams)
                .WithOne(x => x.Department)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // POST
        // ========================================================

        builder.Entity<Post>(entity =>
        {
            entity.HasKey(x => x.PostId);

            entity.Property(x => x.PostName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.Grade)
                .HasMaxLength(20);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            entity.HasIndex(x => new
            {
                x.DepartmentId,
                x.PostName
            })
            .IsUnique();


            // Post ? Exams
            entity.HasMany(x => x.Exams)
                .WithOne(x => x.Post)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // SUBJECT
        // ========================================================
        //
        // Subject is ONLY a master subject.
        //
        // NO:
        // - marks
        // - exam question count
        // - exam-specific configuration
        //
        // Those belong to ExamSubjectPart / ExamQuestion.
        // ========================================================

        builder.Entity<Subject>(entity =>
        {
            entity.HasKey(x => x.SubjectId);

            entity.Property(x => x.SubjectName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            entity.HasIndex(x => x.SubjectName)
                .IsUnique();


            // Subject ? Question Bank
            entity.HasMany(x => x.Questions)
                .WithOne(x => x.Subject)
                .HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);


            // Subject ? Exam Parts
            entity.HasMany(x => x.ExamParts)
                .WithOne(x => x.Subject)
                .HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // QUESTION
        // ========================================================
        //
        // Question contains:
        // - question text
        // - options
        // - correct answer
        //
        // Question DOES NOT contain:
        // - marks
        // - negative marks
        //
        // Marks are assigned when the question enters an Exam.
        // ========================================================

        builder.Entity<Question>(entity =>
        {
            entity.HasKey(x => x.QuestionId);

            entity.Property(x => x.QuestionText)
                .HasMaxLength(3000)
                .IsRequired();

            entity.Property(x => x.ImagePath)
                .HasMaxLength(1000);

            entity.Property(x => x.Explanation)
                .HasMaxLength(3000);

            entity.Property(x => x.OptionA)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.OptionB)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.OptionC)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.OptionD)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.OptionE)
                .HasMaxLength(1000);

            entity.Property(x => x.CorrectAnswer)
                .HasMaxLength(1)
                .IsRequired();

            entity.Property(x => x.Topic)
                .HasMaxLength(150);

            entity.Property(x => x.Tags)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedBy)
                .HasMaxLength(450);

            entity.Property(x => x.UpdatedBy)
                .HasMaxLength(450);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            // Subject ? Questions
            entity.HasOne(x => x.Subject)
                .WithMany(x => x.Questions)
                .HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);


            // Useful indexes for larger subject question banks.
            entity.HasIndex(x => new
            {
                x.SubjectId,
                x.IsActive
            });

            entity.HasIndex(x => new
            {
                x.SubjectId,
                x.DisplayOrder
            });

            entity.HasIndex(x => new { x.IsActive, x.SubjectId, x.DisplayOrder });
        });


        // ========================================================
        // EXAM
        // ========================================================

        builder.Entity<Exam>(entity =>
        {
            entity.HasKey(x => x.ExamId);

            entity.Property(x => x.ExamTitle)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Instructions)
                .HasMaxLength(2000);

            entity.Property(x => x.Audience)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Purpose)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.ClassName)
                .HasMaxLength(150);

            entity.Property(x => x.AcademicLevel)
                .HasMaxLength(50);

            entity.Property(x => x.ClassCode)
                .HasMaxLength(40);
            entity.HasIndex(x => x.ClassCode).IsUnique().HasFilter("[ClassCode] IS NOT NULL");

            entity.Property(x => x.TotalMarks)
                .HasPrecision(10, 2);

            entity.Property(x => x.NegativeMarksPerWrongAnswer)
                .HasPrecision(6, 2);

            entity.Property(x => x.PassingPercentage)
                .HasPrecision(5, 2);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");

            entity.HasIndex(x => new { x.CreatedBy, x.Status });
            entity.HasIndex(x => new { x.Audience, x.IsPublished, x.ExamDate });


            // Exam ? Department
            entity.HasOne(x => x.Department)
                .WithMany(x => x.Exams)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);


            // Exam ? Post
            entity.HasOne(x => x.Post)
                .WithMany(x => x.Exams)
                .HasForeignKey(x => x.PostId)
                .OnDelete(DeleteBehavior.Restrict);


            // Exam ? Subject Parts
            entity.HasMany(x => x.SubjectParts)
                .WithOne(x => x.Exam)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            // Exam ? Selected Questions
            entity.HasMany(x => x.ExamQuestions)
                .WithOne(x => x.Exam)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            // Exam ? Sessions
            entity.HasMany(x => x.Sessions)
                .WithOne(x => x.Exam)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            // Exam ? Student Attempts
            //
            // Attempts must be preserved for result/proctoring history.
            entity.HasMany(x => x.Attempts)
                .WithOne(x => x.Exam)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // EXAM SUBJECT PART
        // ========================================================
        //
        // Exam-specific subject configuration.
        //
        // Example:
        //
        // English
        // General Knowledge
        // Mathematics
        //
        // But NO fixed number of questions is required here.
        // ========================================================

        builder.Entity<ExamSubjectPart>(entity =>
        {
            entity.HasKey(x => x.ExamSubjectPartId);

            entity.Property(x => x.DefaultMarksPerQuestion)
                .HasPrecision(8, 2);

            entity.Property(x => x.DefaultNegativeMarks)
                .HasPrecision(8, 2);


            entity.HasIndex(x => new
            {
                x.ExamId,
                x.SubjectId
            })
            .IsUnique();


            entity.HasOne(x => x.Exam)
                .WithMany(x => x.SubjectParts)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            entity.HasOne(x => x.Subject)
                .WithMany(x => x.ExamParts)
                .HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // EXAM QUESTION
        // ========================================================
        //
        // THIS IS WHERE MARKS LIVE.
        //
        // Question Bank:
        //     Question ? NO MARKS
        //
        // Exam:
        //     ExamQuestion ? Marks / NegativeMarks
        //
        // ========================================================

        builder.Entity<ExamQuestion>(entity =>
        {
            entity.HasKey(x => x.ExamQuestionId);

            entity.Property(x => x.Marks)
                .HasPrecision(8, 2);

            entity.Property(x => x.NegativeMarks)
                .HasPrecision(8, 2);


            // Same question cannot be selected twice
            // in the same exam.
            entity.HasIndex(x => new
            {
                x.ExamId,
                x.QuestionId
            })
            .IsUnique();


            // Exam ? ExamQuestion
            entity.HasOne(x => x.Exam)
                .WithMany(x => x.ExamQuestions)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            // Question ? ExamQuestion
            //
            // Question bank must never be deleted automatically
            // because an old exam may still depend on it.
            entity.HasOne(x => x.Question)
                .WithMany(x => x.ExamQuestions)
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);


            // ====================================================
            // IMPORTANT FIX
            // ====================================================
            //
            // DO NOT use SetNull here.
            //
            // Exam ? Sessions
            // Exam ? ExamQuestions
            // ExamQuestion ? Session
            //
            // SetNull creates a SQL Server multiple-cascade-path
            // situation.
            //
            // Restrict/NoAction is safer.
            //
            entity.HasOne(x => x.Session)
                .WithMany(x => x.ExamQuestions)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // EXAM SESSION
        // ========================================================

        builder.Entity<ExamSession>(entity =>
        {
            entity.HasKey(x => x.SessionId);

            entity.Property(x => x.SessionTitle)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.Instructions)
                .HasMaxLength(1000);


            entity.HasIndex(x => new
            {
                x.ExamId,
                x.SessionNumber
            })
            .IsUnique();


            // Exam ? Sessions
            entity.HasOne(x => x.Exam)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);


            // Session ? Subject summaries
            entity.HasMany(x => x.SessionSubjects)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);


            // Session ? Student session attempts
            //
            // Do not automatically destroy attempt history.
            entity.HasMany(x => x.Attempts)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // EXAM SESSION SUBJECT
        // ========================================================

        builder.Entity<ExamSessionSubject>(entity =>
        {
            entity.HasKey(x => x.SessionSubjectId);

            entity.Property(x => x.Marks)
                .HasPrecision(10, 2);


            entity.HasIndex(x => new
            {
                x.SessionId,
                x.SubjectId
            })
            .IsUnique();


            // Session ? SessionSubject
            entity.HasOne(x => x.Session)
                .WithMany(x => x.SessionSubjects)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);


            // Subject ? SessionSubject
            entity.HasOne(x => x.Subject)
                .WithMany()
                .HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        // ========================================================
        // EXAM ATTEMPT
        // ========================================================

        builder.Entity<ExamAttempt>(entity =>
        {
            entity.HasKey(x => x.AttemptId);

            entity.Property(x => x.TotalMarksObtained)
                .HasPrecision(10, 2);

            entity.Property(x => x.TotalMarks)
                .HasPrecision(10, 2);

            entity.Property(x => x.Percentage)
                .HasPrecision(6, 2);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.StartedAt)
                .HasDefaultValueSql("GETDATE()");


            // Student ? Attempts
            entity.HasOne(x => x.Student)
                .WithMany()
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);


            // Exam ? Attempts
            entity.HasOne(x => x.Exam)
                .WithMany(x => x.Attempts)
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Restrict);


            // Attempt ? Session Attempts
            entity.HasMany(x => x.SessionAttempts)
                .WithOne(x => x.Attempt)
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            // Attempt ? Result
            entity.HasOne(x => x.Result)
                .WithOne(x => x.Attempt)
                .HasForeignKey<ExamResult>(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            // Keep all practice attempts; Start uses a serializable transaction
            // to prevent concurrent active attempts for the same exam/student.
            entity.HasIndex(x => new
            {
                x.StudentId,
                x.ExamId
            });

            entity.HasIndex(x => new { x.StudentId, x.Status });
        });


        // ========================================================
        // EXAM SESSION ATTEMPT
        // ========================================================

        builder.Entity<ExamSessionAttempt>(entity =>
        {
            entity.HasKey(x => x.SessionAttemptId);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.MarksObtained)
                .HasPrecision(10, 2);

            entity.Property(x => x.TotalMarks)
                .HasPrecision(10, 2);

            entity.Property(x => x.LastQuestionId);


            // Attempt ? SessionAttempt
            entity.HasOne(x => x.Attempt)
                .WithMany(x => x.SessionAttempts)
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            // Session ? SessionAttempt
            //
            // Restrict keeps historical attempt records safe.
            entity.HasOne(x => x.Session)
                .WithMany(x => x.Attempts)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);


            // SessionAttempt ? Answers
            entity.HasMany(x => x.Answers)
                .WithOne(x => x.SessionAttempt)
                .HasForeignKey(x => x.SessionAttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            // A session can occur once inside an attempt.
            entity.HasIndex(x => new
            {
                x.AttemptId,
                x.SessionId
            })
            .IsUnique();
        });


        // ========================================================
        // STUDENT ANSWER
        // ========================================================

        builder.Entity<StudentAnswer>(entity =>
        {
            entity.HasKey(x => x.AnswerId);

            entity.Property(x => x.SelectedOption)
                .HasMaxLength(1);

            entity.Property(x => x.MarksAwarded)
                .HasPrecision(10, 2);

            entity.Property(x => x.AnsweredAt)
                .HasDefaultValueSql("GETDATE()");


            // SessionAttempt ? Answers
            entity.HasOne(x => x.SessionAttempt)
                .WithMany(x => x.Answers)
                .HasForeignKey(x => x.SessionAttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            // Question ? StudentAnswers
            //
            // Never delete answer history automatically when
            // a question is removed from the question bank.
            entity.HasOne(x => x.Question)
                .WithMany(x => x.StudentAnswers)
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);


            // One answer per question in one session attempt.
            entity.HasIndex(x => new
            {
                x.SessionAttemptId,
                x.QuestionId
            })
            .IsUnique();
        });


        // ========================================================
        // EXAM RESULT
        // ========================================================

        builder.Entity<ExamResult>(entity =>
        {
            entity.HasKey(x => x.ResultId);

            entity.Property(x => x.TotalMarksObtained)
                .HasPrecision(10, 2);

            entity.Property(x => x.TotalMarks)
                .HasPrecision(10, 2);

            entity.Property(x => x.Percentage)
                .HasPrecision(6, 2);

            entity.Property(x => x.CertificateNumber)
                .HasMaxLength(100);

            entity.Property(x => x.GeneratedDate)
                .HasDefaultValueSql("GETDATE()");


            entity.HasOne(x => x.Attempt)
                .WithOne(x => x.Result)
                .HasForeignKey<ExamResult>(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);


            entity.HasIndex(x => x.AttemptId)
                .IsUnique();


            // Certificate number is optional,
            // but when present it must be unique.
            entity.HasIndex(x => x.CertificateNumber)
                .IsUnique()
                .HasFilter("[CertificateNumber] IS NOT NULL");
        });




        // ========================================================
        // EXAM ASSIGNMENT / STUDENT NOTIFICATIONS
        // ========================================================
        builder.Entity<ExamAssignment>(entity =>
        {
            entity.HasKey(x => x.ExamAssignmentId);
            entity.Property(x => x.RollNumber).HasMaxLength(40);
            entity.HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
            entity.HasIndex(x => new { x.ExamId, x.RollNumber }).IsUnique().HasFilter("[RollNumber] IS NOT NULL");
            entity.HasOne(x => x.Exam).WithMany(x => x.Assignments).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Student).WithMany(x => x.ExamAssignments).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StudentNotification>(entity =>
        {
            entity.HasKey(x => x.StudentNotificationId);
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(700).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(80);
            entity.HasIndex(x => new { x.StudentId, x.IsRead, x.CreatedDate });
            entity.HasOne(x => x.Student).WithMany(x => x.Notifications).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Exam).WithMany(x => x.Notifications).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.SetNull);
        });

        // ========================================================
        // PROCTORING LOG
        // ========================================================

        builder.Entity<ProctoringLog>(entity =>
        {
            entity.HasKey(x => x.LogId);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.ScreenshotPath)
                .HasMaxLength(500);

            entity.Property(x => x.RecordingPath)
                .HasMaxLength(500);

            entity.Property(x => x.ConfidenceScore)
                .HasPrecision(5, 4);

            entity.Property(x => x.ModelName)
                .HasMaxLength(100);

            entity.Property(x => x.ModelVersion)
                .HasMaxLength(100);

            entity.Property(x => x.ClientEventId)
                .HasMaxLength(64);

            entity.Property(x => x.AlertType)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Severity)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.CreatedDate)
                .HasDefaultValueSql("GETDATE()");


            // Student ? Proctoring Logs
            entity.HasOne(x => x.Student)
                .WithMany()
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);


            // Exam ? Proctoring Logs
            entity.HasOne(x => x.Exam)
                .WithMany()
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.SetNull);


            // Attempt ? Proctoring Logs
            entity.HasOne(x => x.Attempt)
                .WithMany()
                .HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.SetNull);


            // Exact active session associated with the browser event.
            entity.HasOne(x => x.SessionAttempt)
                .WithMany()
                .HasForeignKey(x => x.SessionAttemptId)
                // SQL Server sees SetNull as a cascading action. Because an
                // ExamAttempt already reaches ProctoringLogs directly and via
                // ExamSessionAttempts, SetNull creates a multiple cascade path.
                // Keep audit logs immutable and require explicit cleanup instead.
                .OnDelete(DeleteBehavior.NoAction);


            entity.HasIndex(x => x.StudentId);
            entity.HasIndex(x => x.ExamId);
            entity.HasIndex(x => x.AttemptId);
            entity.HasIndex(x => new { x.AttemptId, x.AlertType, x.CreatedDate });
            entity.HasIndex(x => x.SessionAttemptId);
            entity.HasIndex(x => x.CreatedDate);
            entity.HasIndex(x => x.ClientEventId)
                .IsUnique()
                .HasFilter("[ClientEventId] IS NOT NULL");
        });

        builder.Entity<StudentEducation>(entity =>
        {
            entity.HasKey(x => x.StudentEducationId);
            entity.Property(x => x.Percentage).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.StudentId, x.DisplayOrder });
            entity.HasOne(x => x.Student).WithMany(x => x.Educations).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StudentExperience>(entity =>
        {
            entity.HasKey(x => x.StudentExperienceId);
            entity.HasIndex(x => new { x.StudentId, x.StartDate });
            entity.HasOne(x => x.Student).WithMany(x => x.Experiences).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StudentCertification>(entity =>
        {
            entity.HasKey(x => x.StudentCertificationId);
            entity.HasIndex(x => new { x.StudentId, x.CreatedDate });
            entity.HasOne(x => x.Student).WithMany(x => x.Certifications).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
