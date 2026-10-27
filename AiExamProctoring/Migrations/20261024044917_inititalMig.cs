using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiExamProctoring.Migrations
{
    /// <inheritdoc />
    public partial class inititalMig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdmissionAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    ApplicationId = table.Column<int>(type: "int", nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    SubjectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubjectName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.SubjectId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(75)", maxLength: 75, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(75)", maxLength: 75, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RollNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CNIC = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    FatherName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    Batch = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Semester = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Session = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    University = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProfileImage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProfilePhotoCapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HighestQualification = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrentStatus = table.Column<int>(type: "int", nullable: true),
                    OrganizationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Domicile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DomicileTaluka = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnionCouncil = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DomicileNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PrcNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DomicileIssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PermanentAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CandidateNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TestingOrganization = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    PostName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.PostId);
                    table.ForeignKey(
                        name: "FK_Posts_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: true),
                    OptionA = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OptionB = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OptionC = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OptionD = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OptionE = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectAnswer = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.QuestionId);
                    table.ForeignKey(
                        name: "FK_Questions_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcademicClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TeacherId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademicClasses_AspNetUsers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentCertifications",
                columns: table => new
                {
                    StudentCertificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InstituteName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Duration = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CertificatePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCertifications", x => x.StudentCertificationId);
                    table.ForeignKey(
                        name: "FK_StudentCertifications_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentEducations",
                columns: table => new
                {
                    StudentEducationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    QualificationLevel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DegreeTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InstituteName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    BoardUniversity = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PassingYear = table.Column<int>(type: "int", nullable: true),
                    GradeCgpaDivision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    MajorSubjects = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    TranscriptPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentEducations", x => x.StudentEducationId);
                    table.ForeignKey(
                        name: "FK_StudentEducations_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentExperiences",
                columns: table => new
                {
                    StudentExperienceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EmploymentType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCurrentlyWorking = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentExperiences", x => x.StudentExperienceId);
                    table.ForeignKey(
                        name: "FK_StudentExperiences_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UniversityCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Campus = table.Column<int>(type: "int", nullable: false),
                    RollNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ProtectedTemporaryPassword = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniversityCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UniversityCandidates_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcademicClassId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassMemberships_AcademicClasses_AcademicClassId",
                        column: x => x.AcademicClassId,
                        principalTable: "AcademicClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassMemberships_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionLimit = table.Column<int>(type: "int", nullable: true),
                    RecruitmentDepartment = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RecruitmentPost = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RecruitmentGrade = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AcademicClassId = table.Column<int>(type: "int", nullable: true),
                    ViolationAction = table.Column<int>(type: "int", nullable: false),
                    BlockingMode = table.Column<int>(type: "int", nullable: false),
                    ExamTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AcademicLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClassCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsReusable = table.Column<bool>(type: "bit", nullable: false),
                    MaximumAttempts = table.Column<int>(type: "int", nullable: false),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    PostId = table.Column<int>(type: "int", nullable: true),
                    TotalQuestions = table.Column<int>(type: "int", nullable: false),
                    TotalMarks = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    NegativeMarksPerWrongAnswer = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    PassingPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    NumberOfSessions = table.Column<int>(type: "int", nullable: false),
                    ExamDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EntryWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    ShuffleQuestions = table.Column<bool>(type: "bit", nullable: false),
                    ShuffleOptions = table.Column<bool>(type: "bit", nullable: false),
                    AllowBackNavigation = table.Column<bool>(type: "bit", nullable: false),
                    AutoSubmitOnTimeEnd = table.Column<bool>(type: "bit", nullable: false),
                    RequireFullscreen = table.Column<bool>(type: "bit", nullable: false),
                    RequireCamera = table.Column<bool>(type: "bit", nullable: false),
                    RequireMicrophone = table.Column<bool>(type: "bit", nullable: false),
                    AdmissionsOpen = table.Column<bool>(type: "bit", nullable: false),
                    ApplicationFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ApplicationDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultsPublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultsPublishedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.ExamId);
                    table.ForeignKey(
                        name: "FK_Exams_AcademicClasses_AcademicClassId",
                        column: x => x.AcademicClassId,
                        principalTable: "AcademicClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exams_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "PostId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdmissionApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Campus = table.Column<int>(type: "int", nullable: false),
                    Qualification = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Institution = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ObtainedMarks = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    MaximumMarks = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    FeeDue = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiptFile = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerificationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InvigilatorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdmissionApplications_AspNetUsers_InvigilatorId",
                        column: x => x.InvigilatorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdmissionApplications_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdmissionApplications_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamAssignments",
                columns: table => new
                {
                    ExamAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RollNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAssignments", x => x.ExamAssignmentId);
                    table.ForeignKey(
                        name: "FK_ExamAssignments_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamAssignments_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalMarksObtained = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    TotalMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    ResultReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultReviewedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedForViolations = table.Column<bool>(type: "bit", nullable: false),
                    TotalViolations = table.Column<int>(type: "int", nullable: false),
                    StrikeCount = table.Column<int>(type: "int", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    LastViolationAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TeacherResumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HighSeverityViolations = table.Column<int>(type: "int", nullable: false),
                    IsDisqualified = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamInvigilators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvitedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    TeacherId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamInvigilators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_AspNetUsers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamInvigilators_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSessions",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SessionNumber = table.Column<int>(type: "int", nullable: false),
                    SessionTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    RestAfterMinutes = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_ExamSessions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamSubjectParts",
                columns: table => new
                {
                    ExamSubjectPartId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DefaultMarksPerQuestion = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    DefaultNegativeMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubjectParts", x => x.ExamSubjectPartId);
                    table.ForeignKey(
                        name: "FK_ExamSubjectParts_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSubjectParts_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamTeamMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    SenderId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTeamMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTeamMessages_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentNotifications",
                columns: table => new
                {
                    StudentNotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentNotifications", x => x.StudentNotificationId);
                    table.ForeignKey(
                        name: "FK_StudentNotifications_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentNotifications_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AttemptIdentityPhotos",
                columns: table => new
                {
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    ImageBytes = table.Column<byte[]>(type: "varbinary(max)", maxLength: 5000000, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptIdentityPhotos", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_AttemptIdentityPhotos_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamResults",
                columns: table => new
                {
                    ResultId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    TotalMarksObtained = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    TotalMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    IsPassed = table.Column<bool>(type: "bit", nullable: false),
                    ReviewDeduction = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CorrectAnswers = table.Column<int>(type: "int", nullable: false),
                    WrongAnswers = table.Column<int>(type: "int", nullable: false),
                    SkippedAnswers = table.Column<int>(type: "int", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GeneratedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamResults", x => x.ResultId);
                    table.ForeignKey(
                        name: "FK_ExamResults_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                columns: table => new
                {
                    ExamQuestionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    Marks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    NegativeMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => x.ExamQuestionId);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSessionAttempts",
                columns: table => new
                {
                    SessionAttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceHealthJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MarksObtained = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    TotalMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    CorrectAnswers = table.Column<int>(type: "int", nullable: false),
                    WrongAnswers = table.Column<int>(type: "int", nullable: false),
                    SkippedAnswers = table.Column<int>(type: "int", nullable: false),
                    LastQuestionId = table.Column<int>(type: "int", nullable: true),
                    CurrentQuestionIndex = table.Column<int>(type: "int", nullable: false),
                    RemainingTimeSeconds = table.Column<int>(type: "int", nullable: true),
                    SelectedAnswersJSON = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FrozenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProctoringIncidentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessionAttempts", x => x.SessionAttemptId);
                    table.ForeignKey(
                        name: "FK_ExamSessionAttempts_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSessionAttempts_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSessionSubjects",
                columns: table => new
                {
                    SessionSubjectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    QuestionsToShow = table.Column<int>(type: "int", nullable: false),
                    Marks = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessionSubjects", x => x.SessionSubjectId);
                    table.ForeignKey(
                        name: "FK_ExamSessionSubjects_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSessionSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProctoringLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VideoEvidencePath = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    SpeechTranscript = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SemanticSimilarity = table.Column<decimal>(type: "decimal(7,6)", nullable: true),
                    MatchedQuestionId = table.Column<int>(type: "int", nullable: true),
                    SpeakerAttribution = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: true),
                    AttemptId = table.Column<int>(type: "int", nullable: true),
                    SessionAttemptId = table.Column<int>(type: "int", nullable: true),
                    QuestionIdAtCapture = table.Column<int>(type: "int", nullable: true),
                    QuestionIndexAtCapture = table.Column<int>(type: "int", nullable: true),
                    AlertType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ScreenshotPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordingPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordingIncludesAudio = table.Column<bool>(type: "bit", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    DetectedFaceCount = table.Column<int>(type: "int", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    ModelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientEventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FrameCapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CountsAsViolation = table.Column<bool>(type: "bit", nullable: false),
                    PolicyProcessed = table.Column<bool>(type: "bit", nullable: false),
                    PolicyPending = table.Column<bool>(type: "bit", nullable: false),
                    IncidentKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    WarningNumber = table.Column<int>(type: "int", nullable: true),
                    IsReviewed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProctoringLogs", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_ProctoringLogs_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProctoringLogs_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProctoringLogs_ExamSessionAttempts_SessionAttemptId",
                        column: x => x.SessionAttemptId,
                        principalTable: "ExamSessionAttempts",
                        principalColumn: "SessionAttemptId");
                    table.ForeignKey(
                        name: "FK_ProctoringLogs_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudentAnswers",
                columns: table => new
                {
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionAttemptId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    SelectedOption = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    MarksAwarded = table.Column<decimal>(type: "decimal(8,2)", precision: 10, scale: 2, nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    IsMarkedForReview = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAnswers", x => x.AnswerId);
                    table.ForeignKey(
                        name: "FK_StudentAnswers_ExamSessionAttempts_SessionAttemptId",
                        column: x => x.SessionAttemptId,
                        principalTable: "ExamSessionAttempts",
                        principalColumn: "SessionAttemptId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentAnswers_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "QuestionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdmissionQuestionDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    EvidenceLogId = table.Column<int>(type: "int", nullable: false),
                    CheatingConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    DeductedMarks = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReviewerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmissionQuestionDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdmissionQuestionDecisions_ExamAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ExamAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdmissionQuestionDecisions_ProctoringLogs_EvidenceLogId",
                        column: x => x.EvidenceLogId,
                        principalTable: "ProctoringLogs",
                        principalColumn: "LogId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicClasses_TeacherId",
                table: "AcademicClasses",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionApplications_ExamId_InvigilatorId",
                table: "AdmissionApplications",
                columns: new[] { "ExamId", "InvigilatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionApplications_ExamId_StudentId",
                table: "AdmissionApplications",
                columns: new[] { "ExamId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionApplications_InvigilatorId",
                table: "AdmissionApplications",
                column: "InvigilatorId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionApplications_PaymentReference",
                table: "AdmissionApplications",
                column: "PaymentReference",
                unique: true,
                filter: "[PaymentReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionApplications_StudentId",
                table: "AdmissionApplications",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionAudits_ExamId_AtUtc",
                table: "AdmissionAudits",
                columns: new[] { "ExamId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionQuestionDecisions_AttemptId_QuestionId",
                table: "AdmissionQuestionDecisions",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmissionQuestionDecisions_EvidenceLogId",
                table: "AdmissionQuestionDecisions",
                column: "EvidenceLogId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DepartmentId",
                table: "AspNetUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClassMemberships_AcademicClassId_StudentId",
                table: "ClassMemberships",
                columns: new[] { "AcademicClassId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassMemberships_StudentId",
                table: "ClassMemberships",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_DepartmentName",
                table: "Departments",
                column: "DepartmentName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_ExamId_RollNumber",
                table: "ExamAssignments",
                columns: new[] { "ExamId", "RollNumber" },
                unique: true,
                filter: "[RollNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_ExamId_StudentId",
                table: "ExamAssignments",
                columns: new[] { "ExamId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamAssignments_StudentId",
                table: "ExamAssignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId",
                table: "ExamAttempts",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_StudentId_ExamId",
                table: "ExamAttempts",
                columns: new[] { "StudentId", "ExamId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_StudentId_Status",
                table: "ExamAttempts",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_ExamId_TeacherId",
                table: "ExamInvigilators",
                columns: new[] { "ExamId", "TeacherId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamInvigilators_TeacherId",
                table: "ExamInvigilators",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_ExamId_QuestionId",
                table: "ExamQuestions",
                columns: new[] { "ExamId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_QuestionId",
                table: "ExamQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_SessionId",
                table: "ExamQuestions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_AttemptId",
                table: "ExamResults",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_CertificateNumber",
                table: "ExamResults",
                column: "CertificateNumber",
                unique: true,
                filter: "[CertificateNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AcademicClassId",
                table: "Exams",
                column: "AcademicClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_Audience_IsPublished_ExamDate",
                table: "Exams",
                columns: new[] { "Audience", "IsPublished", "ExamDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_ClassCode",
                table: "Exams",
                column: "ClassCode",
                unique: true,
                filter: "[ClassCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CreatedBy_Status",
                table: "Exams",
                columns: new[] { "CreatedBy", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_DepartmentId",
                table: "Exams",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_PostId",
                table: "Exams",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessionAttempts_AttemptId_SessionId",
                table: "ExamSessionAttempts",
                columns: new[] { "AttemptId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessionAttempts_SessionId",
                table: "ExamSessionAttempts",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_ExamId_SessionNumber",
                table: "ExamSessions",
                columns: new[] { "ExamId", "SessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessionSubjects_SessionId_SubjectId",
                table: "ExamSessionSubjects",
                columns: new[] { "SessionId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessionSubjects_SubjectId",
                table: "ExamSessionSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectParts_ExamId_SubjectId",
                table: "ExamSubjectParts",
                columns: new[] { "ExamId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectParts_SubjectId",
                table: "ExamSubjectParts",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTeamMessages_ExamId",
                table: "ExamTeamMessages",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_DepartmentId_PostName",
                table: "Posts",
                columns: new[] { "DepartmentId", "PostName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_AttemptId",
                table: "ProctoringLogs",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_AttemptId_AlertType_CreatedDate",
                table: "ProctoringLogs",
                columns: new[] { "AttemptId", "AlertType", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_ClientEventId",
                table: "ProctoringLogs",
                column: "ClientEventId",
                unique: true,
                filter: "[ClientEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_CreatedDate",
                table: "ProctoringLogs",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_ExamId",
                table: "ProctoringLogs",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_PolicyPending_PolicyProcessed",
                table: "ProctoringLogs",
                columns: new[] { "PolicyPending", "PolicyProcessed" },
                filter: "[PolicyPending] = 1 AND [PolicyProcessed] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_SessionAttemptId",
                table: "ProctoringLogs",
                column: "SessionAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_ProctoringLogs_StudentId",
                table: "ProctoringLogs",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_IsActive_SubjectId_DisplayOrder",
                table: "Questions",
                columns: new[] { "IsActive", "SubjectId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubjectId_DisplayOrder",
                table: "Questions",
                columns: new[] { "SubjectId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubjectId_IsActive",
                table: "Questions",
                columns: new[] { "SubjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_QuestionId",
                table: "StudentAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_SessionAttemptId_QuestionId",
                table: "StudentAnswers",
                columns: new[] { "SessionAttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentCertifications_StudentId_CreatedDate",
                table: "StudentCertifications",
                columns: new[] { "StudentId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentEducations_StudentId_DisplayOrder",
                table: "StudentEducations",
                columns: new[] { "StudentId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentExperiences_StudentId_StartDate",
                table: "StudentExperiences",
                columns: new[] { "StudentId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotifications_ExamId",
                table: "StudentNotifications",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentNotifications_StudentId_IsRead_CreatedDate",
                table: "StudentNotifications",
                columns: new[] { "StudentId", "IsRead", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SubjectName",
                table: "Subjects",
                column: "SubjectName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UniversityCandidates_RollNumber",
                table: "UniversityCandidates",
                column: "RollNumber",
                unique: true,
                filter: "[RollNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UniversityCandidates_StudentId",
                table: "UniversityCandidates",
                column: "StudentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdmissionApplications");

            migrationBuilder.DropTable(
                name: "AdmissionAudits");

            migrationBuilder.DropTable(
                name: "AdmissionQuestionDecisions");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AttemptIdentityPhotos");

            migrationBuilder.DropTable(
                name: "ClassMemberships");

            migrationBuilder.DropTable(
                name: "ExamAssignments");

            migrationBuilder.DropTable(
                name: "ExamInvigilators");

            migrationBuilder.DropTable(
                name: "ExamQuestions");

            migrationBuilder.DropTable(
                name: "ExamResults");

            migrationBuilder.DropTable(
                name: "ExamSessionSubjects");

            migrationBuilder.DropTable(
                name: "ExamSubjectParts");

            migrationBuilder.DropTable(
                name: "ExamTeamMessages");

            migrationBuilder.DropTable(
                name: "StudentAnswers");

            migrationBuilder.DropTable(
                name: "StudentCertifications");

            migrationBuilder.DropTable(
                name: "StudentEducations");

            migrationBuilder.DropTable(
                name: "StudentExperiences");

            migrationBuilder.DropTable(
                name: "StudentNotifications");

            migrationBuilder.DropTable(
                name: "UniversityCandidates");

            migrationBuilder.DropTable(
                name: "ProctoringLogs");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "ExamSessionAttempts");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "ExamSessions");

            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.DropTable(
                name: "AcademicClasses");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Departments");
        }
    }
}
