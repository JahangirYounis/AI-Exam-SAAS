using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(
            ApplicationDbContext context,
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ILogger logger)
        {
            await SeedRolesAsync(roleManager, logger);
            await SeedDepartmentsAndPostsAsync(context, logger);
            await QuestionBankSeed.SeedAsync(context, logger);
            await PakistanQuestionBankExpansionSeed.SeedAsync(context, logger);
            await SeedAdminAsync(userManager, configuration, logger);
            // University admissions exposes entry tests only; retain legacy records without re-seeding demo tests.
        }

        private static async Task SeedRolesAsync(
            RoleManager<IdentityRole> roleManager,
            ILogger logger)
        {
            string[] roles = { "Admin", "Teacher", "Student" };

            foreach (var role in roles)
            {
                if (await roleManager.RoleExistsAsync(role))
                    continue;

                var result = await roleManager.CreateAsync(new IdentityRole(role));

                if (!result.Succeeded)
                {
                    logger.LogError(
                        "Unable to create role {Role}: {Errors}",
                        role,
                        string.Join("; ", result.Errors.Select(x => x.Description)));
                }
            }
        }

        private static async Task SeedDepartmentsAndPostsAsync(
            ApplicationDbContext context,
            ILogger logger)
        {
            var seed = new[]
            {
                new
                {
                    Name = "Education Department",
                    Code = "EDU",
                    Description = "Education and school teaching related examinations.",
                    Posts = new[]
                    {
                        (Name: "Junior Science Teacher", Grade: "BPS-14"),
                        (Name: "Junior Elementary Teacher", Grade: "BPS-14"),
                        (Name: "Primary School Teacher", Grade: "BPS-14")
                    }
                },
                new
                {
                    Name = "Police Department",
                    Code = "POL",
                    Description = "Police recruitment and departmental examinations.",
                    Posts = new[]
                    {
                        (Name: "Police Constable", Grade: "BPS-07"),
                        (Name: "Assistant Sub Inspector", Grade: "BPS-09"),
                        (Name: "Sub Inspector", Grade: "BPS-14")
                    }
                },
                new
                {
                    Name = "Health Department",
                    Code = "HEA",
                    Description = "Health and medical recruitment examinations.",
                    Posts = new[]
                    {
                        (Name: "Staff Nurse", Grade: "BPS-16"),
                        (Name: "Medical Officer", Grade: "BPS-17")
                    }
                },
                new
                {
                    Name = "Revenue Department",
                    Code = "REV",
                    Description = "Revenue administration and recruitment examinations.",
                    Posts = new[]
                    {
                        (Name: "Mukhtiarkar", Grade: "BPS-17"),
                        (Name: "Assistant Mukhtiarkar", Grade: "BPS-16")
                    }
                },
                new
                {
                    Name = "Excise, Taxation & Narcotics Control Department",
                    Code = "ETNC",
                    Description = "Excise, taxation and narcotics-control recruitment examinations.",
                    Posts = new[]
                    {
                        (Name: "Narcotics Control Constable", Grade: "BPS-07"),
                        (Name: "Excise & Taxation Officer", Grade: "BPS-17")
                    }
                },
                new
                {
                    Name = "Federal Public Service Commission",
                    Code = "FPSC",
                    Description = "Federal competitive and general recruitment examinations.",
                    Posts = new[]
                    {
                        (Name: "Central Superior Services Candidate", Grade: "BS-17"),
                        (Name: "Assistant Director", Grade: "BS-17"),
                        (Name: "Inspector", Grade: "BS-16")
                    }
                },
                new
                {
                    Name = "Provincial Public Service Commission",
                    Code = "PPSC",
                    Description = "Provincial competitive and departmental recruitment examinations.",
                    Posts = new[]
                    {
                        (Name: "Combined Competitive Examination Candidate", Grade: "BPS-17"),
                        (Name: "Section Officer", Grade: "BPS-17"),
                        (Name: "Lecturer", Grade: "BPS-17")
                    }
                },
                new
                {
                    Name = "Local Government Department",
                    Code = "LGD",
                    Description = "Municipal, local council and local administration recruitment.",
                    Posts = new[]
                    {
                        (Name: "Town Officer", Grade: "BPS-16"),
                        (Name: "Municipal Officer", Grade: "BPS-16"),
                        (Name: "Junior Clerk", Grade: "BPS-11")
                    }
                },
                new
                {
                    Name = "Information Technology Department",
                    Code = "ITD",
                    Description = "Software, network, data and information-technology recruitment.",
                    Posts = new[]
                    {
                        (Name: "Software Engineer", Grade: "BPS-17"),
                        (Name: "Network Administrator", Grade: "BPS-16"),
                        (Name: "Data Entry Operator", Grade: "BPS-12")
                    }
                },
                new
                {
                    Name = "Banking & Finance",
                    Code = "BNK",
                    Description = "Banking, finance, audit and trainee recruitment tests.",
                    Posts = new[]
                    {
                        (Name: "Management Trainee Officer", Grade: "OG-II"),
                        (Name: "Cash Officer", Grade: "OG-III"),
                        (Name: "Audit Officer", Grade: "OG-II")
                    }
                },
                new
                {
                    Name = "Universities & Higher Education",
                    Code = "HEI",
                    Description = "University admission, scholarship and higher-education employment tests.",
                    Posts = new[]
                    {
                        (Name: "Undergraduate Admission Candidate", Grade: "Admission"),
                        (Name: "Graduate Admission Candidate", Grade: "Admission"),
                        (Name: "Teaching Assistant", Grade: "Contract")
                    }
                },
                new
                {
                    Name = "Armed Forces & Defence Services",
                    Code = "DEF",
                    Description = "Initial academic and aptitude screening for defence-service candidates.",
                    Posts = new[]
                    {
                        (Name: "Officer Candidate", Grade: "Commission"),
                        (Name: "Technical Cadet", Grade: "Cadet"),
                        (Name: "Clerk / Administrative Trade", Grade: "Trade")
                    }
                }
            };

            foreach (var item in seed)
            {
                var department = await context.Departments
                    .FirstOrDefaultAsync(x => x.DepartmentName == item.Name);

                if (department == null)
                {
                    department = new Department
                    {
                        DepartmentName = item.Name,
                        DepartmentCode = item.Code,
                        Description = item.Description,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    };

                    context.Departments.Add(department);
                    await context.SaveChangesAsync();

                    logger.LogInformation("Seeded department {Department}.", item.Name);
                }
                else
                {
                    if (!department.IsActive)
                    {
                        department.IsActive = true;
                        await context.SaveChangesAsync();
                    }
                }

                foreach (var postSeed in item.Posts)
                {
                    var exists = await context.Posts.AnyAsync(x =>
                        x.DepartmentId == department.DepartmentId &&
                        x.PostName == postSeed.Name);

                    if (exists)
                        continue;

                    context.Posts.Add(new Post
                    {
                        DepartmentId = department.DepartmentId,
                        PostName = postSeed.Name,
                        Grade = postSeed.Grade,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }

                await context.SaveChangesAsync();
            }
        }

        private static async Task SeedAdminAsync(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ILogger logger)
        {
            var email = configuration["BootstrapAdmin:Email"]?.Trim();
            var password = configuration["BootstrapAdmin:Password"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Bootstrap admin credentials are not configured. Existing admin accounts are unaffected; fresh installations should configure BootstrapAdmin:Email and BootstrapAdmin:Password using User Secrets or environment variables.");
                return;
            }

            var admin = await userManager.FindByEmailAsync(email);

            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FirstName = "System",
                    LastName = "Administrator",
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    PhoneNumberConfirmed = true,
                    IsApproved = true,
                    IsBlocked = false,
                    Department = "Administration",
                    Batch = "Admin",
                    Semester = "-",
                    CreatedDate = DateTime.Now
                };

                var result = await userManager.CreateAsync(admin, password);

                if (!result.Succeeded)
                {
                    logger.LogError(
                        "Unable to create default admin: {Errors}",
                        string.Join("; ", result.Errors.Select(x => x.Description)));
                    return;
                }
            }

            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                var roleResult = await userManager.AddToRoleAsync(admin, "Admin");

                if (!roleResult.Succeeded)
                {
                    logger.LogError(
                        "Unable to assign Admin role: {Errors}",
                        string.Join("; ", roleResult.Errors.Select(x => x.Description)));
                }
            }
        }

        private static async Task SeedReusablePublicTestAsync(
            ApplicationDbContext context,
            ILogger logger)
        {
            const string title = "Online Examination System Demo Test";
            const int requiredQuestionCount = 10;
            const int requiredSessionCount = 2;
            var adminId = await (from user in context.Users
                                 join userRole in context.UserRoles on user.Id equals userRole.UserId
                                 join role in context.Roles on userRole.RoleId equals role.Id
                                 where role.Name == "Admin" && user.IsApproved && !user.IsBlocked
                                 orderby user.CreatedDate
                                 select user.Id)
                .FirstOrDefaultAsync();

            var exam = await context.Exams
                .Include(x => x.ExamQuestions)
                .Include(x => x.SubjectParts)
                .Include(x => x.Sessions)
                .FirstOrDefaultAsync(x => x.IsReusable &&
                                          x.ClassName == "Open Public Testing Lab");

            if (exam is null)
            {
                exam = new Exam
                {
                    ExamTitle = title,
                    Description = "Explore the examination experience with an always-open practice paper. Learn the device checks, questions, submission and results before your scheduled test.",
                    Instructions = "You may repeat this practice paper after completing your current attempt. Camera, microphone and fullscreen checks help you prepare for a scheduled examination.",
                    Audience = ExamAudience.Public,
                    Purpose = ExamPurpose.Practice,
                    ClassName = "Open Public Testing Lab",
                    IsReusable = true,
                    MaximumAttempts = 20,
                    TotalMarks = requiredQuestionCount,
                    PassingPercentage = 40m,
                    DurationMinutes = 20,
                    NumberOfSessions = requiredSessionCount,
                    ExamDate = DateTime.Today,
                    StartTime = TimeSpan.Zero,
                    EntryWindowMinutes = 1440,
                    ShuffleQuestions = true,
                    ShuffleOptions = true,
                    AllowBackNavigation = true,
                    AutoSubmitOnTimeEnd = true,
                    RequireFullscreen = true,
                    RequireCamera = true,
                    RequireMicrophone = true,
                    IsPublished = true,
                    IsLocked = true,
                    Status = ExamStatus.Running,
                    CreatedBy = adminId ?? "System Seed",
                    CreatedDate = DateTime.Now
                };
                context.Exams.Add(exam);
                await context.SaveChangesAsync();
            }
            else
            {
                // Preserve an existing seeded test while applying the new product name.
                exam.ExamTitle = title;
                exam.IsReusable = true;
                exam.MaximumAttempts = Math.Max(20, exam.MaximumAttempts);
                exam.Audience = ExamAudience.Public;
                exam.Purpose = ExamPurpose.Practice;
                exam.IsPublished = true;
                exam.IsLocked = true;
                exam.Status = ExamStatus.Running;
                exam.ExamDate = DateTime.Today;
                exam.StartTime = TimeSpan.Zero;
                exam.EntryWindowMinutes = 1440;
                exam.NumberOfSessions = requiredSessionCount;
                exam.DurationMinutes = 20;
                exam.CreatedBy = adminId ?? "System Seed";
                exam.UpdatedBy = adminId;
                exam.UpdatedDate = DateTime.Now;
                exam.Description = "Explore the examination experience with an always-open practice paper. Learn the device checks, questions, submission and results before your scheduled test.";
                exam.Instructions = "You may repeat this practice paper after completing your current attempt. Camera, microphone and fullscreen checks help you prepare for a scheduled examination.";
            }

            var activeQuestions = exam.ExamQuestions
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.ExamQuestionId)
                .ToList();
            var activeSessions = exam.Sessions
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.SessionId)
                .ToList();

            if (activeQuestions.Count == requiredQuestionCount && activeSessions.Count == requiredSessionCount)
            {
                exam.TotalQuestions = requiredQuestionCount;
                exam.TotalMarks = requiredQuestionCount;
                exam.NumberOfSessions = requiredSessionCount;
                await context.SaveChangesAsync();
                return;
            }

            if (exam.ExamQuestions.Count > 0 || exam.SubjectParts.Count > 0 || exam.Sessions.Count > 0)
            {
                var hasAttempts = await context.ExamAttempts.AsNoTracking().AnyAsync(x => x.ExamId == exam.ExamId);
                if (hasAttempts)
                {
                    var retainedQuestions = exam.ExamQuestions
                        .OrderByDescending(x => x.IsActive)
                        .ThenBy(x => x.DisplayOrder)
                        .ThenBy(x => x.ExamQuestionId)
                        .Take(requiredQuestionCount)
                        .ToList();
                    if (retainedQuestions.Count < requiredQuestionCount)
                    {
                        logger.LogWarning("Admin demo test has attempts and only {Count}/{RequiredCount} stored questions, so its paper could not be safely resized.", retainedQuestions.Count, requiredQuestionCount);
                        return;
                    }

                    foreach (var oldSession in exam.Sessions)
                        oldSession.IsActive = false;

                    var replacementSessions = Enumerable.Range(1, requiredSessionCount).Select(number => new ExamSession
                    {
                        ExamId = exam.ExamId,
                        SessionNumber = number,
                        SessionTitle = $"Demo Session {number}",
                        Instructions = "Complete all five questions while the secure monitoring checks remain active.",
                        DurationMinutes = 10,
                        RestAfterMinutes = number == 1 ? 2 : 0,
                        DisplayOrder = number,
                        IsActive = true,
                        IsLocked = true,
                        CreatedDate = DateTime.Now
                    }).ToList();
                    context.ExamSessions.AddRange(replacementSessions);
                    await context.SaveChangesAsync();

                    foreach (var storedQuestion in exam.ExamQuestions)
                        storedQuestion.IsActive = retainedQuestions.Contains(storedQuestion);
                    for (var index = 0; index < retainedQuestions.Count; index++)
                    {
                        retainedQuestions[index].SessionId = replacementSessions[index / 5].SessionId;
                        retainedQuestions[index].Marks = 1m;
                        retainedQuestions[index].NegativeMarks = 0m;
                        retainedQuestions[index].DisplayOrder = index + 1;
                    }

                    var retainedQuestionIds = retainedQuestions.Select(x => x.QuestionId).ToList();
                    var retainedSubjects = await context.Questions.AsNoTracking()
                        .Where(x => retainedQuestionIds.Contains(x.QuestionId))
                        .Select(x => new { x.QuestionId, x.SubjectId })
                        .ToListAsync();
                    foreach (var part in exam.SubjectParts)
                        part.IsActive = retainedSubjects.Any(x => x.SubjectId == part.SubjectId);
                    foreach (var sessionGroup in retainedQuestions.Select((item, index) => new
                             {
                                 Session = replacementSessions[index / 5],
                                 SubjectId = retainedSubjects.First(x => x.QuestionId == item.QuestionId).SubjectId
                             }).GroupBy(x => new { x.Session, x.SubjectId }))
                    {
                        context.ExamSessionSubjects.Add(new ExamSessionSubject
                        {
                            SessionId = sessionGroup.Key.Session.SessionId,
                            SubjectId = sessionGroup.Key.SubjectId,
                            QuestionsToShow = sessionGroup.Count(),
                            Marks = sessionGroup.Count(),
                            DisplayOrder = sessionGroup.Key.SubjectId,
                            IsActive = true,
                            CreatedDate = DateTime.Now
                        });
                    }

                    exam.TotalQuestions = requiredQuestionCount;
                    exam.TotalMarks = requiredQuestionCount;
                    exam.NumberOfSessions = requiredSessionCount;
                    await context.SaveChangesAsync();
                    logger.LogInformation("Resized the admin demo test without deleting records referenced by existing attempts.");
                    return;
                }

                context.ExamQuestions.RemoveRange(exam.ExamQuestions);
                context.ExamSubjectParts.RemoveRange(exam.SubjectParts);
                context.ExamSessions.RemoveRange(exam.Sessions);
                await context.SaveChangesAsync();
                exam.ExamQuestions.Clear();
                exam.SubjectParts.Clear();
                exam.Sessions.Clear();
            }

            var subjectNames = new[] { "English", "Mathematics", "General Science", "Pakistan Studies", "Computer Science" };
            var selectedQuestions = new List<Question>();
            foreach (var subjectName in subjectNames)
            {
                selectedQuestions.AddRange(await context.Questions.AsNoTracking()
                    .Where(x => x.IsActive && x.IsVerified && x.Subject != null && x.Subject.SubjectName == subjectName)
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.QuestionId)
                    .Take(2)
                    .ToListAsync());
            }

            if (selectedQuestions.Count < requiredQuestionCount)
            {
                logger.LogWarning("Reusable public system test was not created because only {Count}/{RequiredCount} starter questions were available.", selectedQuestions.Count, requiredQuestionCount);
                await context.SaveChangesAsync();
                return;
            }

            var sessions = Enumerable.Range(1, requiredSessionCount).Select(number => new ExamSession
            {
                ExamId = exam.ExamId,
                SessionNumber = number,
                SessionTitle = $"Demo Session {number}",
                Instructions = "Complete all five questions while the secure monitoring checks remain active.",
                DurationMinutes = 10,
                RestAfterMinutes = number == 1 ? 2 : 0,
                DisplayOrder = number,
                IsActive = true,
                IsLocked = true,
                CreatedDate = DateTime.Now
            }).ToList();
            context.ExamSessions.AddRange(sessions);
            await context.SaveChangesAsync();

            var order = 0;
            foreach (var question in selectedQuestions)
            {
                var session = sessions[order / 5];
                context.ExamQuestions.Add(new ExamQuestion
                {
                    ExamId = exam.ExamId,
                    QuestionId = question.QuestionId,
                    SessionId = session.SessionId,
                    Marks = 1m,
                    NegativeMarks = 0m,
                    DisplayOrder = ++order,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
            }

            var subjectOrder = 0;
            foreach (var group in selectedQuestions.GroupBy(x => x.SubjectId))
            {
                context.ExamSubjectParts.Add(new ExamSubjectPart
                {
                    ExamId = exam.ExamId,
                    SubjectId = group.Key,
                    DisplayOrder = ++subjectOrder,
                    DefaultMarksPerQuestion = 1m,
                    DefaultNegativeMarks = 0m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
                foreach (var sessionGroup in group.Select((question, index) => new { question, index })
                             .GroupBy(x => sessions[selectedQuestions.IndexOf(x.question) / 5]))
                {
                    context.ExamSessionSubjects.Add(new ExamSessionSubject
                    {
                        SessionId = sessionGroup.Key.SessionId,
                        SubjectId = group.Key,
                        QuestionsToShow = sessionGroup.Count(),
                        Marks = sessionGroup.Count(),
                        DisplayOrder = subjectOrder,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }
            }

            exam.TotalQuestions = requiredQuestionCount;
            exam.TotalMarks = requiredQuestionCount;
            exam.NumberOfSessions = requiredSessionCount;
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded admin-managed public demo test with {QuestionCount} questions in {SessionCount} sessions.", requiredQuestionCount, requiredSessionCount);
        }
    }
}
