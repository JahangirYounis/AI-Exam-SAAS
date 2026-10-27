using AiExamProctoring.Hubs;
using Microsoft.AspNetCore.SignalR;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Data;
using System.Security.Claims;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher"), Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExamController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ExamController> _logger;
    private readonly IHubContext<TeacherCollaborationHub>? _collaboration;

    public ExamController(ApplicationDbContext db, IEmailSender emailSender, ILogger<ExamController> logger, IHubContext<TeacherCollaborationHub>? collaboration = null)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
        _collaboration = collaboration;
    }

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<string> GenerateClassCodeAsync()
    {
        string code;
        do { code = "TEST-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)); }
        while (await _db.Exams.AsNoTracking().AnyAsync(x => x.ClassCode == code));
        return code;
    }

    public async Task<IActionResult> Index(bool past = false)
    {
        ViewBag.Past = past;
        var query = _db.Exams.AsNoTracking().Include(x => x.Department).Include(x => x.Post)
            .Where(x => x.CreatedBy == TeacherId);
        query = past ? query.Where(x => x.Status == ExamStatus.Completed || x.Status == ExamStatus.Cancelled)
            : query.Where(x => x.Status != ExamStatus.Completed && x.Status != ExamStatus.Cancelled);
        return View(await query.OrderByDescending(x => x.CreatedDate).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create(ExamPurpose? purpose = null, ExamAudience? audience = null)
    {
        await Load();
        return View(new Exam
        {
            QuestionLimit = 10,
            ExamDate = DateTime.Today.AddDays(1),
            StartTime = new TimeSpan(10, 0, 0),
            NumberOfSessions = 1,
            DurationMinutes = 60,
            TotalMarks = 100m,
            PassingPercentage = 40m,
            EntryWindowMinutes = 30,
            MaximumAttempts = 1,
            AcademicLevel = null,
            Purpose = purpose.HasValue && Enum.IsDefined(purpose.Value) ? purpose.Value : ExamPurpose.AcademicClass,
            Audience = audience ?? ExamAudience.Private
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("ExamId,ExamTitle,QuestionLimit,RecruitmentDepartment,RecruitmentPost,RecruitmentGrade,DepartmentId,PostId,AcademicClassId,Audience,Purpose,ClassName,AcademicLevel,IsReusable,TotalMarks,NegativeMarksPerWrongAnswer,PassingPercentage,DurationMinutes,NumberOfSessions,ExamDate,StartTime,EntryWindowMinutes,ShuffleOptions,BlockingMode")] Exam m, string? invigilatorEmails = null, bool inviteQuestions = false, bool inviteMonitoring = false)
    {
        if (string.IsNullOrWhiteSpace(TeacherId)) return Challenge();
        await ValidateAsync(m);
        var emails = (invigilatorEmails ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(e => e.ToUpperInvariant()).Distinct().ToArray();
        var teacherRoles = _db.Roles.Where(r => r.Name == "Teacher").Select(r => r.Id);
        var approvedTeachers = _db.UserRoles.Where(r => teacherRoles.Contains(r.RoleId)).Select(r => r.UserId);
        var invitees = await _db.Users.Where(u => emails.Contains(u.NormalizedEmail!) && approvedTeachers.Contains(u.Id) && u.Id != TeacherId && u.IsApproved && !u.IsBlocked).ToListAsync();
        if (emails.Length > 0 && !inviteQuestions && !inviteMonitoring) ModelState.AddModelError("invigilatorEmails", "Choose at least one collaborator permission.");
        if (emails.Length > 20 || invitees.Count != emails.Length) ModelState.AddModelError("invigilatorEmails", "Enter up to 20 other approved teachers' registered emails.");
        if (!ModelState.IsValid)
        {
            await Load(m.DepartmentId, m.PostId);
            return View(m);
        }

        NormalizeTargeting(m);
        m.CreatedDate = DateTime.Now; m.CreatedBy = TeacherId; m.Status = ExamStatus.Draft;
        // Publication and related records are created by their own checked workflows.
        m.ExamId = 0; m.IsPublished = false; m.IsLocked = false;
        m.UpdatedBy = null; m.UpdatedDate = null; m.TotalQuestions = 0;
        m.SubjectParts = new List<ExamSubjectPart>(); m.ExamQuestions = new List<ExamQuestion>();
        m.Sessions = new List<ExamSession>(); m.Attempts = new List<ExamAttempt>();
        m.Assignments = new List<ExamAssignment>(); m.Notifications = new List<StudentNotification>();
        m.Department = null; m.Post = null; m.AcademicClass = null;
        if (m.Audience == ExamAudience.Private)
            m.ClassCode = await GenerateClassCodeAsync();
        if (m.Audience == ExamAudience.Public) m.ClassCode = null;

        try
        {
            _db.Exams.Add(m);
            foreach (var teacher in invitees) _db.ExamInvigilators.Add(new ExamInvigilator { Exam = m, TeacherId = teacher.Id, IsActive = false, CanEditQuestions = inviteQuestions, CanMonitor = inviteMonitoring });
            await _db.SaveChangesAsync();
            foreach (var teacher in invitees)
            {
                try
                {
                    if (_collaboration is not null) await _collaboration.Clients.Group("collaboration:" + teacher.Id).SendAsync("CollaborationChanged", new { message = "You received an exam invitation. Open Exam invitations to accept." });
                    if (teacher.Email is not null) await _emailSender.SendEmailAsync(teacher.Email, "Exam collaboration invitation", "You have an examination collaboration invitation. Sign in to your examination portal and open Teacher / Invitations to review and accept.");
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Invitation saved but delivery failed for teacher {TeacherId}", teacher.Id); TempData["Error"] = "Invitations were saved; some email or live notifications could not be delivered."; }
            }
            TempData["Success"] = "Draft saved. Choose questions, then review and publish your test.";
            return RedirectToAction("Index", "ExamQuestion", new { examId = m.ExamId });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Teacher {TeacherId} could not create examination {Title}.", TeacherId, m.ExamTitle);
            ModelState.AddModelError(string.Empty,
                "The examination could not be saved to the database. No data was lost. Check the highlighted fields and try again; if the issue continues, ask the administrator to verify the database migration and connection.");
            await Load(m.DepartmentId, m.PostId);
            return View(m);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var m = await _db.Exams.FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId); if (m == null) return NotFound();
        if (m.IsLocked || m.IsPublished || await _db.ExamAttempts.AnyAsync(x => x.ExamId == id)) { TempData["Error"] = "Published/locked exams cannot be edited."; return RedirectToAction(nameof(Details), new { id }); }
        await Load(m.DepartmentId, m.PostId); return View(m);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("ExamId,ExamTitle,QuestionLimit,RecruitmentDepartment,RecruitmentPost,RecruitmentGrade,DepartmentId,PostId,AcademicClassId,Audience,Purpose,ClassName,AcademicLevel,IsReusable,TotalMarks,NegativeMarksPerWrongAnswer,PassingPercentage,DurationMinutes,NumberOfSessions,ExamDate,StartTime,EntryWindowMinutes,ShuffleOptions,BlockingMode")] Exam m)
    {
        if (id != m.ExamId) return NotFound(); var db = await _db.Exams.Include(x => x.ExamQuestions).Include(x => x.Sessions).FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId); if (db == null) return NotFound();
        if (db.IsLocked || db.IsPublished || await _db.ExamAttempts.AnyAsync(x => x.ExamId == id)) { TempData["Error"] = "Published/locked exams cannot be edited."; return RedirectToAction(nameof(Details), new { id }); }
        if (string.IsNullOrWhiteSpace(TeacherId)) return Challenge();
        await ValidateAsync(m);
        var activeQuestionCount = db.ExamQuestions.Count(x => x.IsActive);
        if (activeQuestionCount > 0 && m.TotalMarks < activeQuestionCount * 0.01m)
            ModelState.AddModelError(nameof(m.TotalMarks), $"Total Marks must be at least {activeQuestionCount * 0.01m:0.00} for {activeQuestionCount} selected questions.");
        if (!ModelState.IsValid) { await Load(m.DepartmentId, m.PostId); return View(m); }
        NormalizeTargeting(m);
        try
        {
            var markingChanged = db.TotalMarks != m.TotalMarks ||
                                 db.NegativeMarksPerWrongAnswer != m.NegativeMarksPerWrongAnswer;
            db.QuestionLimit = m.QuestionLimit; db.RecruitmentDepartment = m.RecruitmentDepartment; db.RecruitmentPost = m.RecruitmentPost; db.RecruitmentGrade = m.RecruitmentGrade;
            db.ExamTitle = m.ExamTitle.Trim(); db.DepartmentId = m.DepartmentId; db.PostId = m.PostId;
            db.AcademicClassId = m.AcademicClassId; db.ViolationAction = m.ViolationAction; db.Audience = m.Audience; db.Purpose = m.Purpose; db.ClassName = m.ClassName?.Trim(); db.AcademicLevel = m.AcademicLevel; db.IsReusable = m.IsReusable; db.MaximumAttempts = m.MaximumAttempts;
            if (db.Audience == ExamAudience.Private && string.IsNullOrWhiteSpace(db.ClassCode))
                db.ClassCode = await GenerateClassCodeAsync();
            if (db.Audience == ExamAudience.Public) db.ClassCode = null;
            db.TotalMarks = m.TotalMarks; db.NegativeMarksPerWrongAnswer = m.NegativeMarksPerWrongAnswer; db.PassingPercentage = m.PassingPercentage; db.DurationMinutes = m.DurationMinutes; db.NumberOfSessions = m.NumberOfSessions; db.ExamDate = m.ExamDate; db.StartTime = m.StartTime; db.EntryWindowMinutes = m.EntryWindowMinutes;
            db.ShuffleQuestions = m.ShuffleQuestions; db.ShuffleOptions = m.ShuffleOptions; db.AutoSubmitOnTimeEnd = m.AutoSubmitOnTimeEnd; db.BlockingMode = m.BlockingMode; db.RequireFullscreen = m.RequireFullscreen; db.RequireCamera = m.RequireCamera; db.RequireMicrophone = m.RequireMicrophone;
            if (markingChanged && db.ExamQuestions.Any(x => x.IsActive))
            {
                foreach (var q in db.ExamQuestions) q.SessionId = null;
                RedistributeExamMarks(db.ExamQuestions.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ToList(), db.TotalMarks, db.NegativeMarksPerWrongAnswer);
                await _db.SaveChangesAsync();
                if (db.Sessions.Any()) _db.ExamSessions.RemoveRange(db.Sessions);
            }
            db.Status = db.ExamQuestions.Any(x => x.IsActive) ? ExamStatus.Building : ExamStatus.Draft; db.UpdatedDate = DateTime.Now; db.UpdatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier); await _db.SaveChangesAsync();
            TempData["Success"] = "Exam updated successfully."; return RedirectToAction(nameof(Details), new { id });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Teacher {TeacherId} could not update examination {ExamId}.", TeacherId, id);
            ModelState.AddModelError(string.Empty, "The examination changes could not be saved. Check the fields and try again; if it continues, ask the administrator to verify the database.");
            await Load(m.DepartmentId, m.PostId);
            return View(m);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        // IMPORTANT PERFORMANCE NOTE:
        // Do NOT eager-load every collection in one giant Include graph here.
        // ExamQuestions x SubjectParts x Sessions x Assignments creates a cartesian
        // result set that grows very quickly as the paper becomes larger and can
        // trigger SQL execution timeouts. Load the independent collections with
        // small split queries and compose the navigation graph in memory instead.
        var exam = await _db.Exams
            .AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId, cancellationToken);

        if (exam == null)
            return NotFound();

        var subjectParts = await _db.ExamSubjectParts
            .AsNoTracking()
            .Include(x => x.Subject)
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        var examQuestions = await _db.ExamQuestions
            .AsNoTracking()
            .Include(x => x.Question)
                .ThenInclude(x => x!.Subject)
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.ExamQuestionId)
            .ToListAsync(cancellationToken);

        var sessions = await _db.ExamSessions
            .AsNoTracking()
            .Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.SessionId)
            .ToListAsync(cancellationToken);

        // The ExamQuestion rows already contain SessionId. Reuse those rows instead
        // of loading ExamQuestions again through every Session navigation.
        foreach (var session in sessions)
        {
            session.ExamQuestions = examQuestions
                .Where(x => x.SessionId == session.SessionId)
                .ToList();
        }

        var assignments = await _db.ExamAssignments
            .AsNoTracking()
            .Where(x => x.ExamId == id)
            .ToListAsync(cancellationToken);

        exam.SubjectParts = subjectParts;
        exam.ExamQuestions = examQuestions;
        exam.Sessions = sessions;
        exam.Assignments = assignments;

        var studentRoleId = await _db.Roles
            .AsNoTracking()
            .Where(x => x.Name == "Student")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        ViewBag.EligibleStudentCount = string.IsNullOrWhiteSpace(studentRoleId)
            ? 0
            : await (from user in _db.Users.AsNoTracking()
                     join ur in _db.UserRoles.AsNoTracking() on user.Id equals ur.UserId
                     where ur.RoleId == studentRoleId && user.IsApproved && !user.IsBlocked
                     select user.Id)
                .Distinct()
                .CountAsync(cancellationToken);

        return View(exam);
    }

    [HttpGet]
    public async Task<IActionResult> Candidates(int id, string? search, CancellationToken cancellationToken)
    {
        var exam = await _db.Exams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId, cancellationToken);
        if (exam is null) return NotFound();
        if (exam.AcademicClassId.HasValue)
        {
            TempData["Error"] = "Candidates are managed through this exam's class roster.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (exam.Audience != ExamAudience.Private)
        {
            TempData["Error"] = "Public examinations are open to every approved student and do not need a private candidate list.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var studentRoleId = await _db.Roles.AsNoTracking()
            .Where(x => x.Name == "Student").Select(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var query = from user in _db.Users.AsNoTracking()
                    join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                    where userRole.RoleId == studentRoleId && user.IsApproved && !user.IsBlocked
                    select user;
        if (exam.AcademicClassId.HasValue)
            query = query.Where(user => _db.ClassMemberships.Any(m =>
                m.AcademicClassId == exam.AcademicClassId && m.StudentId == user.Id && m.IsActive &&
                m.AcademicClass != null && m.AcademicClass.IsActive));
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(x => x.FullName.Contains(search) ||
                                     (x.Email != null && x.Email.Contains(search)) ||
                                     (x.CNIC != null && x.CNIC.Contains(search)) ||
                                     (x.CandidateNumber != null && x.CandidateNumber.Contains(search)));
        }

        ViewBag.CandidateTotal = await query.Distinct().CountAsync(cancellationToken);
        ViewBag.Exam = exam;
        ViewBag.SelectedIds = (await _db.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == id && x.IsActive)
            .Select(x => x.StudentId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return View(await query.Distinct().OrderBy(x => x.FullName).Take(500).ToListAsync(cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCandidates(int id, string[]? studentIds, CancellationToken cancellationToken)
    {
        var exam = await _db.Exams.Include(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId, cancellationToken);
        if (exam is null) return NotFound();
        if (exam.AcademicClassId.HasValue)
        {
            TempData["Error"] = "Candidates are managed through this exam's class roster.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (exam.Audience != ExamAudience.Private || exam.IsPublished || exam.IsLocked)
        {
            TempData["Error"] = "Only an unpublished private examination can change its candidate list.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var requested = (studentIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (requested.Count > 5000) return BadRequest("Select no more than 5000 candidates per test.");
        var studentRoleId = await _db.Roles.AsNoTracking()
            .Where(x => x.Name == "Student").Select(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var eligible = await (from user in _db.Users.AsNoTracking()
                              join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                              where userRole.RoleId == studentRoleId && user.IsApproved && !user.IsBlocked && requested.Contains(user.Id)
                              select user).Distinct().ToListAsync(cancellationToken);
        if (exam.AcademicClassId.HasValue)
        {
            var roster = await _db.ClassMemberships.Where(m => m.AcademicClassId == exam.AcademicClassId && m.IsActive && m.AcademicClass!.IsActive).Select(m => m.StudentId).ToListAsync(cancellationToken);
            if (eligible.Any(u => !roster.Contains(u.Id))) return BadRequest("Only approved members of this class can be assigned.");
        }
        var accepted = eligible.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (accepted.Count != requested.Count)
        {
            TempData["Error"] = "Some selected accounts are no longer eligible. Review the list and save again.";
            return RedirectToAction(nameof(Candidates), new { id });
        }

        foreach (var assignment in exam.Assignments)
            assignment.IsActive = accepted.Contains(assignment.StudentId);

        var existing = exam.Assignments.ToDictionary(x => x.StudentId, StringComparer.Ordinal);
        var nextSequence = exam.Assignments.Count + 1;
        foreach (var student in eligible)
        {
            if (existing.TryGetValue(student.Id, out var assignment))
            {
                assignment.IsActive = true;
                assignment.AssignedAt = DateTime.Now;
                assignment.AssignedBy = TeacherId;
            }
            else
            {
                assignment = new ExamAssignment
                {
                    ExamId = id,
                    StudentId = student.Id,
                    AssignedAt = DateTime.Now,
                    AssignedBy = TeacherId,
                    RollNumber = $"EG-{id:000000}-{nextSequence++:00000}",
                    IsActive = true
                };
                _db.ExamAssignments.Add(assignment);
            }

            var alreadyNotified = await _db.StudentNotifications.AsNoTracking().AnyAsync(x =>
                x.StudentId == student.Id && x.ExamId == id && x.Type == "ExamAssignment", cancellationToken);
            if (!alreadyNotified)
            {
                _db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = student.Id,
                    ExamId = id,
                    Title = "Private examination assigned",
                    Message = $"{exam.ExamTitle} has been assigned to you. Scheduled start: {exam.StartDate:dd MMM yyyy, hh:mm tt}.",
                    Type = "ExamAssignment",
                    CreatedDate = DateTime.Now
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"Private candidate list saved: {accepted.Count} approved student(s) assigned.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [NonAction]
    public async Task<IActionResult> AssignAll(int id)
    {
        var exam = await _db.Exams
            .AsSplitQuery()
            .Include(x => x.ExamQuestions)
                .ThenInclude(x => x.Question)
            .Include(x => x.SubjectParts)
            .Include(x => x.Sessions)
                .ThenInclude(x => x.ExamQuestions)
            .Include(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId);

        if (exam == null)
            return NotFound();

        if (exam.IsLocked || exam.IsPublished)
        {
            TempData["Error"] = "Published or locked examinations cannot be reassigned.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var selectedQuestions = exam.ExamQuestions
            .Where(x => x.IsActive && x.Question != null && x.Question.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.ExamQuestionId)
            .ToList();

        if (selectedQuestions.Count == 0)
        {
            TempData["Error"] = "Add questions to the examination before assigning candidates.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (exam.NumberOfSessions < 1 || exam.NumberOfSessions > 20)
        {
            TempData["Error"] = "Number of Sessions must be between 1 and 20.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (exam.DurationMinutes < exam.NumberOfSessions)
        {
            TempData["Error"] = "Total Duration must allow at least one minute for each session.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (exam.NumberOfSessions > selectedQuestions.Count)
        {
            TempData["Error"] = $"The examination has {selectedQuestions.Count} questions but {exam.NumberOfSessions} sessions. Each session must contain at least one question.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Assignment is intentionally one-click. If the paper changed after session
        // generation, rebuild the session distribution automatically before assigning.
        // This removes the confusing situation where the Assign button looks disabled
        // even though the Teacher has already selected all questions.
        var hasSessionAttempts = await _db.ExamSessionAttempts
            .AsNoTracking()
            .AnyAsync(x => x.Session != null && x.Session.ExamId == id);

        if (!IsSessionDistributionCurrent(exam, selectedQuestions.Count))
        {
            if (hasSessionAttempts)
            {
                TempData["Error"] = "The question paper changed after students started this examination, so sessions cannot be regenerated safely. Create a new examination for the revised paper.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await RebuildSessionsAsync(exam, selectedQuestions);
        }

        var studentRoleId = await _db.Roles
            .Where(x => x.Name == "Student")
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(studentRoleId))
        {
            TempData["Error"] = "Student role is not configured. Run the application initializer and try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Department/Post on an examination describes the examination/recruitment
        // context; it is NOT the student's academic department. Therefore 'Assign all
        // eligible' means every approved, non-blocked account in the Student role.
        var eligibleStudents = await (from user in _db.Users.AsNoTracking()
                                      join ur in _db.UserRoles.AsNoTracking()
                                          on user.Id equals ur.UserId
                                      where ur.RoleId == studentRoleId
                                            && user.IsApproved
                                            && !user.IsBlocked
                                      select user)
            .Distinct()
            .ToListAsync();

        if (exam.AcademicClassId.HasValue)
        {
            var roster = await _db.ClassMemberships.Where(m => m.AcademicClassId == exam.AcademicClassId && m.IsActive && m.AcademicClass!.IsActive).Select(m => m.StudentId).ToListAsync();
            eligibleStudents = eligibleStudents.Where(u => roster.Contains(u.Id)).ToList();
        }
        var studentIds = eligibleStudents.Select(x => x.Id).ToList();

        if (studentIds.Count == 0)
        {
            TempData["Error"] = "No eligible students were found. Approve at least one Student account and make sure it is not blocked.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var eligibleSet = studentIds.ToHashSet(StringComparer.Ordinal);

        // Keep assignment state aligned with current eligibility.
        foreach (var assignment in exam.Assignments)
        {
            assignment.IsActive = eligibleSet.Contains(assignment.StudentId);
        }

        var existingByStudent = exam.Assignments
            .ToDictionary(x => x.StudentId, StringComparer.Ordinal);

        var usedRollNumbers = exam.Assignments
            .Where(x => !string.IsNullOrWhiteSpace(x.RollNumber))
            .Select(x => x.RollNumber!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nextRollSequence = 1;
        string NextRollNumber()
        {
            string value;
            do
            {
                value = $"EG-{exam.ExamId:000000}-{nextRollSequence:00000}";
                nextRollSequence++;
            }
            while (usedRollNumbers.Contains(value));

            usedRollNumbers.Add(value);
            return value;
        }

        var teacherId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var newAssignments = 0;
        var reactivatedAssignments = 0;

        foreach (var studentId in studentIds)
        {
            if (existingByStudent.TryGetValue(studentId, out var existingAssignment))
            {
                if (!existingAssignment.IsActive)
                    reactivatedAssignments++;

                existingAssignment.IsActive = true;
                existingAssignment.AssignedAt = DateTime.Now;
                existingAssignment.AssignedBy = teacherId;

                if (string.IsNullOrWhiteSpace(existingAssignment.RollNumber))
                    existingAssignment.RollNumber = NextRollNumber();

                continue;
            }

            var rollNumber = NextRollNumber();

            var assignment = new ExamAssignment
            {
                ExamId = id,
                StudentId = studentId,
                RollNumber = rollNumber,
                AssignedBy = teacherId,
                AssignedAt = DateTime.Now,
                IsActive = true
            };

            _db.ExamAssignments.Add(assignment);
            existingByStudent[studentId] = assignment;
            newAssignments++;
        }

        var now = DateTime.Now;
        // Assignment is the release point for candidates. Once the paper and sessions
        // are ready, publish automatically so candidates do not remain invisible due
        // to a forgotten second button. Late publishing is allowed only while the
        // configured entry window is still open.
        var canReleaseNow = now <= exam.LatestStartDate;
        if (canReleaseNow)
        {
            exam.IsPublished = true;
            exam.IsLocked = true;
            exam.Status = now >= exam.StartDate ? ExamStatus.Running : ExamStatus.Scheduled;

            // Preserve historical paper integrity. A question that has appeared in a
            // released examination becomes immutable; corrections should be entered
            // as a new question-bank record rather than rewriting past exam history.
            foreach (var selectedQuestion in selectedQuestions)
            {
                if (selectedQuestion.Question is not null)
                    selectedQuestion.Question.IsLocked = true;
            }
        }
        else
        {
            exam.Status = ExamStatus.Ready;
        }

        var existingNotifications = await _db.StudentNotifications
            .Where(n => n.ExamId == id && n.Type == "ExamAssignment" && studentIds.Contains(n.StudentId))
            .ToListAsync();
        var notificationsByStudent = existingNotifications
            .GroupBy(n => n.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedDate).First(), StringComparer.Ordinal);

        foreach (var student in eligibleStudents)
        {
            if (!existingByStudent.TryGetValue(student.Id, out var assignment) || !assignment.IsActive)
                continue;

            var scheduleText = $"{exam.StartDate:dddd, dd MMM yyyy 'at' hh:mm tt}";
            var message = $"{exam.ExamTitle} has been assigned to you for {scheduleText}. Exam roll number: {assignment.RollNumber}. You may enter from the scheduled start time until {exam.LatestStartDate:hh:mm tt}. Open AI Based Online Examination System before the exam to complete camera and microphone checks.";

            if (notificationsByStudent.TryGetValue(student.Id, out var notification))
            {
                notification.Title = canReleaseNow ? "Examination assigned and released" : "Examination assigned";
                notification.Message = message;
                notification.CreatedDate = now;
                notification.IsRead = false;
                notification.ReadDate = null;
            }
            else
            {
                _db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = student.Id,
                    ExamId = id,
                    Title = canReleaseNow ? "Examination assigned and released" : "Examination assigned",
                    Message = message,
                    Type = "ExamAssignment",
                    CreatedDate = now,
                    IsRead = false
                });
            }
        }

        exam.UpdatedDate = DateTime.Now;
        exam.UpdatedBy = teacherId;

        // A single SaveChanges call is already transactional in EF Core.
        // Do not start a user transaction here because SQL Server retry execution
        // strategy (EnableRetryOnFailure) rejects user-created transactions unless
        // the whole unit is executed through CreateExecutionStrategy().
        await _db.SaveChangesAsync();

        // Email is an optional second notification channel. If SMTP is not configured,
        // SmtpEmailSender logs the condition and the in-app notification still works.
        foreach (var student in eligibleStudents.Where(x => !string.IsNullOrWhiteSpace(x.Email)))
        {
            try
            {
                var assignment = existingByStudent[student.Id];
                var subject = $"Exam assigned: {exam.ExamTitle}";
                var body = $"<p>Dear {WebUtility.HtmlEncode(student.FullName ?? "Candidate")},</p>" +
                           $"<p>Your examination <strong>{WebUtility.HtmlEncode(exam.ExamTitle)}</strong> is scheduled for <strong>{exam.StartDate:dddd, dd MMM yyyy 'at' hh:mm tt}</strong>.</p>" +
                           $"<p>Exam roll number: <strong>{WebUtility.HtmlEncode(assignment.RollNumber ?? "Pending")}</strong></p>" +
                           $"<p>Please sign in to AI Based Online Examination System before the scheduled time and complete the browser, camera and microphone readiness checks.</p>";
                await _emailSender.SendEmailAsync(student.Email!, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exam assignment email could not be sent to student {StudentId}.", student.Id);
            }
        }

        TempData["Success"] = canReleaseNow
            ? $"Examination assigned and published for {studentIds.Count} eligible candidate(s). Each candidate received an in-app notification; email was attempted when configured."
            : $"Examination assigned to {studentIds.Count} eligible candidate(s), but it was not published because the entry window has already closed. Update the schedule before release.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsSessionDistributionCurrent(Exam exam, int selectedQuestionCount)
    {
        var activeSessions = exam.Sessions
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToList();

        if (activeSessions.Count != exam.NumberOfSessions)
            return false;

        if (activeSessions.Sum(x => x.DurationMinutes) != exam.DurationMinutes)
            return false;

        var assignedQuestionIds = activeSessions
            .SelectMany(x => x.ExamQuestions)
            .Where(x => x.IsActive)
            .Select(x => x.ExamQuestionId)
            .Distinct()
            .ToList();

        return assignedQuestionIds.Count == selectedQuestionCount;
    }

    private async Task RebuildSessionsAsync(Exam exam, List<ExamQuestion> selectedQuestions)
    {
        // Detach all question/session links first because the FK uses Restrict.
        foreach (var examQuestion in exam.ExamQuestions)
        {
            examQuestion.SessionId = null;
            examQuestion.Session = null;
        }

        await _db.SaveChangesAsync();

        if (exam.Sessions.Count > 0)
        {
            _db.ExamSessions.RemoveRange(exam.Sessions);
            await _db.SaveChangesAsync();
            exam.Sessions.Clear();
        }

        var totalQuestions = selectedQuestions.Count;
        var sessionCount = exam.NumberOfSessions;
        var baseQuestions = totalQuestions / sessionCount;
        var extraQuestions = totalQuestions % sessionCount;
        var baseMinutes = exam.DurationMinutes / sessionCount;
        var extraMinutes = exam.DurationMinutes % sessionCount;
        var currentQuestionIndex = 0;

        for (var i = 0; i < sessionCount; i++)
        {
            var questionCount = baseQuestions + (i < extraQuestions ? 1 : 0);
            var duration = baseMinutes + (i < extraMinutes ? 1 : 0);

            var session = new ExamSession
            {
                ExamId = exam.ExamId,
                SessionNumber = i + 1,
                SessionTitle = $"Session {i + 1}",
                Instructions = "Answer all questions carefully. Your progress is saved automatically.",
                DurationMinutes = duration,
                RestAfterMinutes = i < sessionCount - 1 ? 10 : 0,
                DisplayOrder = i + 1,
                IsActive = true,
                IsLocked = false,
                CreatedDate = DateTime.Now
            };

            _db.ExamSessions.Add(session);


            var sessionQuestions = selectedQuestions
                .Skip(currentQuestionIndex)
                .Take(questionCount)
                .ToList();

            foreach (var examQuestion in sessionQuestions)
            {
                examQuestion.Session = session;
            }

            var subjectOrder = 1;
            foreach (var subjectGroup in sessionQuestions
                         .Where(x => x.Question != null)
                         .GroupBy(x => x.Question!.SubjectId)
                         .OrderBy(x => x.Key))
            {
                _db.ExamSessionSubjects.Add(new ExamSessionSubject
                {
                    Session = session,
                    SubjectId = subjectGroup.Key,
                    QuestionsToShow = subjectGroup.Count(),
                    Marks = subjectGroup.Sum(x => x.Marks),
                    DisplayOrder = subjectOrder++,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
            }

            currentQuestionIndex += questionCount;
        }

        exam.TotalQuestions = totalQuestions;
        exam.Status = ExamStatus.Ready;
        exam.UpdatedDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        var teacherId = TeacherId;
        if (string.IsNullOrWhiteSpace(teacherId)) return Challenge();
        var ct = HttpContext.RequestAborted;
        IActionResult Invalid(string message)
        {
            TempData["Error"] = message;
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
                var e = await _db.Exams.AsSplitQuery()
                    .Include(x => x.ExamQuestions).ThenInclude(x => x.Question)
                    .Include(x => x.Sessions).ThenInclude(x => x.ExamQuestions)
                    .Include(x => x.Assignments)
                    .FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == teacherId, ct);
                if (e is null) return (IActionResult)NotFound();
                if (e.Status == ExamStatus.Cancelled || (!e.IsReusable && e.Status == ExamStatus.Completed))
                    return Invalid("This test has ended or was cancelled. Create a new test.");
                if (e.IsPublished) return RedirectToAction(nameof(Details), new { id });
                if (e.IsLocked || await _db.ExamAttempts.AnyAsync(a => a.ExamId == id, ct))
                    return Invalid("This test is locked or already has attempts. Its paper cannot be rebuilt.");
                if (!e.IsReusable && DateTime.Now > e.LatestStartDate)
                    return Invalid("The entry window has closed. Update the schedule before publishing.");
                var questions = e.ExamQuestions.Where(q => q.IsActive)
                    .OrderBy(q => q.DisplayOrder).ThenBy(q => q.ExamQuestionId).ToList();
                if (questions.Count == 0) return Invalid("Add at least one question before publishing.");
                if (e.QuestionLimit.HasValue && questions.Count != e.QuestionLimit.Value) return Invalid($"Select exactly {e.QuestionLimit.Value} questions before publishing.");
                var authors = await _db.ExamInvigilators.Where(i => i.ExamId == id && i.IsActive && i.AcceptedAtUtc != null).Select(i => i.TeacherId).ToListAsync(ct);
                if (questions.Any(q => q.Question is null || !q.Question.IsActive ||
                    (q.Question.CreatedBy != teacherId && !authors.Contains(q.Question.CreatedBy!) && !q.Question.IsVerified)))
                    return Invalid("Some selected questions are unavailable. Review the paper before publishing.");
                if (e.NumberOfSessions < 1 || e.NumberOfSessions > 20 ||
                    e.NumberOfSessions > questions.Count || e.DurationMinutes < e.NumberOfSessions)
                    return Invalid("Each section needs at least one question and one minute. Update the section count or add questions.");
                if (e.TotalMarks < questions.Count * .01m || e.DurationMinutes < 1 || e.DurationMinutes > 600)
                    return Invalid("Check the total marks and duration before publishing.");

                if (e.AcademicClassId.HasValue && !await _db.AcademicClasses.AnyAsync(c =>
                    c.Id == e.AcademicClassId && c.TeacherId == teacherId && c.IsActive, ct))
                    return Invalid("The linked class is unavailable. Choose an active class you manage.");
                var studentRoles = _db.Roles.Where(r => r.Name == "Student").Select(r => r.Id);
                var eligible = _db.Users.Where(u => u.IsApproved && !u.IsBlocked &&
                    _db.UserRoles.Any(r => r.UserId == u.Id && studentRoles.Contains(r.RoleId)));
                if (e.AcademicClassId.HasValue)
                    eligible = eligible.Where(u => _db.ClassMemberships.Any(m =>
                        m.AcademicClassId == e.AcademicClassId && m.StudentId == u.Id && m.IsActive));
                var assignedIds = e.Assignments.Where(a => a.IsActive).Select(a => a.StudentId).Distinct().ToList();
                if (e.Audience == ExamAudience.Private)
                {
                    if (assignedIds.Count == 0 && (!e.AcademicClassId.HasValue || !await eligible.AnyAsync(ct)))
                        return Invalid("Select candidates or choose a class with approved members before publishing.");
                    if (await eligible.CountAsync(u => assignedIds.Contains(u.Id), ct) != assignedIds.Count)
                        return Invalid("The candidate list contains accounts that are no longer eligible. Review it before publishing.");
                    if (string.IsNullOrWhiteSpace(e.ClassCode)) e.ClassCode = await GenerateClassCodeAsync();
                }
                if (e.AcademicClassId.HasValue)
                {
                    var selectedRoster = await eligible.Select(u => u.Id).ToListAsync(ct);
                    var sequence = e.Assignments.Count + 1;
                    foreach (var studentId in selectedRoster)
                    {
                        var assignment = e.Assignments.FirstOrDefault(a => a.StudentId == studentId);
                        if (assignment is not null) { assignment.IsActive = true; continue; }
                        e.Assignments.Add(new ExamAssignment { ExamId = id, StudentId = studentId, AssignedBy = teacherId,
                            AssignedAt = DateTime.Now, IsActive = true, RollNumber = $"EG-{id:000000}-{sequence++:00000}" });
                    }
                }
                RedistributeExamMarks(questions, e.TotalMarks, e.NegativeMarksPerWrongAnswer);
                // Preparation and publication share the transaction; there is no separate teacher step.
                await RebuildSessionsAsync(e, questions);
                e.IsPublished = true;
                e.IsLocked = true;
                e.Status = e.IsReusable || DateTime.Now >= e.StartDate ? ExamStatus.Running : ExamStatus.Scheduled;
                e.UpdatedDate = DateTime.Now;
                e.UpdatedBy = teacherId;
                foreach (var item in questions) item.Question!.IsLocked = true;
                var notified = (await _db.StudentNotifications.Where(n => n.ExamId == id && n.Type == "ExamReleased")
                    .Select(n => n.StudentId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
                foreach (var assignment in e.Assignments.Where(a => a.IsActive && !notified.Contains(a.StudentId)))
                    _db.StudentNotifications.Add(new StudentNotification
                    {
                        StudentId = assignment.StudentId,
                        ExamId = id,
                        Type = "ExamReleased",
                        Title = "Your examination is ready",
                        Message = e.IsReusable ? $"{e.ExamTitle} is now available." :
                            $"{e.ExamTitle} starts on {e.StartDate:dd MMM yyyy, hh:mm tt}. Roll number: {assignment.RollNumber}.",
                        CreatedDate = DateTime.Now
                    });
                await _db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                TempData["Success"] = "Test published. Questions and time were automatically divided into sections.";
                return RedirectToAction(nameof(Details), new { id });
            });
        }
        catch (DbUpdateException error)
        {
            _logger.LogError(error, "Unable to publish teacher {TeacherId}'s exam {ExamId}.", teacherId, id);
            return Invalid("The test could not be published. Reload and try again.");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(int id)
    {
        var e = await _db.Exams.FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId); if (e == null) return NotFound(); if (e.IsLocked) { TempData["Error"] = "A published exam is locked to protect the paper."; return RedirectToAction(nameof(Details), new { id }); }
        e.IsPublished = false; e.Status = ExamStatus.Building; await _db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Exams.FirstOrDefaultAsync(x => x.ExamId == id && x.CreatedBy == TeacherId); if (e == null) return NotFound(); if (e.IsLocked || e.IsPublished) { TempData["Error"] = "Locked/published exams cannot be deleted."; return RedirectToAction(nameof(Index)); }
        _db.Exams.Remove(e); await _db.SaveChangesAsync(); TempData["Success"] = "Exam deleted."; return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfigureSections(int id, int count, CancellationToken cancellationToken)
    {
        var exam = await _db.Exams.SingleOrDefaultAsync(e => e.ExamId == id && e.CreatedBy == TeacherId, cancellationToken);
        if (exam is null) return NotFound();
        if (exam.IsPublished || exam.IsLocked || await _db.ExamAttempts.AnyAsync(a => a.ExamId == id, cancellationToken)) return Conflict("Sections cannot change after publication or an attempt.");
        if (count < 1 || count > 20 || count > exam.DurationMinutes || count > exam.QuestionLimit) return BadRequest("Choose 1–20 sections, no more than the question count or duration in minutes.");
        exam.NumberOfSessions = count;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Section count saved. Review the paper before Publish and Lock.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ValidateAsync(Exam e)
    {
        // Required delivery rules are server-owned, not hidden-form choices.
        e.RequireCamera = e.RequireMicrophone = e.RequireFullscreen = true;
        e.ShuffleQuestions = e.AutoSubmitOnTimeEnd = true;
        foreach (var key in new[] { nameof(e.RequireCamera), nameof(e.RequireMicrophone), nameof(e.RequireFullscreen),
                     nameof(e.ShuffleQuestions), nameof(e.AutoSubmitOnTimeEnd) }) ModelState.Remove(key);
        if (e.DurationMinutes < e.NumberOfSessions)
            ModelState.AddModelError(nameof(e.NumberOfSessions), "Allow at least one minute per section.");
        if (e.StartTime < TimeSpan.Zero || e.StartTime >= TimeSpan.FromDays(1))
            ModelState.AddModelError(nameof(e.StartTime), "Choose a valid time of day.");
        if (e.ExamDate.Year > 9990) ModelState.AddModelError(nameof(e.ExamDate), "Choose a valid exam date.");
        // Enforce the automatic outcome independently of posted form values.
        e.ViolationAction = AutomaticViolationAction.LockForReview;
        ModelState.Remove(nameof(e.ViolationAction));
        if (e.AcademicClassId.HasValue)
        {
            var group = await _db.AcademicClasses.AsNoTracking().SingleOrDefaultAsync(c => c.Id == e.AcademicClassId && c.TeacherId == TeacherId && c.IsActive);
            if (group is null) ModelState.AddModelError(nameof(e.AcademicClassId), "Select a class you manage.");
            else { e.ClassName = group.Name; e.Audience = ExamAudience.Private; }
        }
        e.ExamTitle = e.ExamTitle?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(e.ExamTitle)) ModelState.AddModelError(nameof(e.ExamTitle), "Exam title is required.");
        if (e.TotalMarks <= 0) ModelState.AddModelError(nameof(e.TotalMarks), "Total Marks must be greater than zero.");
        if (e.NegativeMarksPerWrongAnswer < 0 || e.NegativeMarksPerWrongAnswer > 100) ModelState.AddModelError(nameof(e.NegativeMarksPerWrongAnswer), "Negative marks must be between 0 and 100 per wrong answer.");
        if (e.NumberOfSessions < 1 || e.NumberOfSessions > 20) ModelState.AddModelError(nameof(e.NumberOfSessions), "Number of Sessions must be between 1 and 20.");
        if (e.DurationMinutes < 1 || e.DurationMinutes > 600) ModelState.AddModelError(nameof(e.DurationMinutes), "Duration must be 1-600 minutes.");
        if (!e.IsReusable && e.ExamDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(e.ExamDate), "Exam date cannot be in the past.");

        var start = e.StartTime >= TimeSpan.Zero && e.StartTime < TimeSpan.FromDays(1) ? e.ExamDate.Date.Add(e.StartTime) : e.ExamDate.Date;
        if (!e.IsReusable && e.ExamDate.Date == DateTime.Today && start <= DateTime.Now)
            ModelState.AddModelError(nameof(e.StartTime), "For a new examination, the start time must be in the future.");

        if (e.EntryWindowMinutes < 0 || e.EntryWindowMinutes > 1440)
            ModelState.AddModelError(nameof(e.EntryWindowMinutes), "Entry window must be between 0 and 1440 minutes.");
        if (!Enum.IsDefined(e.BlockingMode)) ModelState.AddModelError(nameof(e.BlockingMode), "Choose a valid blocking mode.");
        if (!Enum.IsDefined(e.Audience)) ModelState.AddModelError(nameof(e.Audience), "Choose a valid exam access type.");
        if (!Enum.IsDefined(e.Purpose))
            ModelState.AddModelError(nameof(e.Purpose), "Choose a valid test type.");

        if (e.QuestionLimit is null or < 1 or > 1000)
            ModelState.AddModelError(nameof(e.QuestionLimit), "Enter the number of questions (1–1000).");
        if (e.ExamId > 0 && await _db.ExamQuestions.CountAsync(q => q.ExamId == e.ExamId && q.IsActive) > e.QuestionLimit)
            ModelState.AddModelError(nameof(e.QuestionLimit), "Remove extra selected questions before reducing this limit.");
        if (e.Purpose == ExamPurpose.Recruitment)
        {
            if (string.IsNullOrWhiteSpace(e.RecruitmentDepartment)) ModelState.AddModelError(nameof(e.RecruitmentDepartment), "Enter the recruiting department.");
            if (string.IsNullOrWhiteSpace(e.RecruitmentPost)) ModelState.AddModelError(nameof(e.RecruitmentPost), "Enter the job title.");
            if (string.IsNullOrWhiteSpace(e.RecruitmentGrade)) ModelState.AddModelError(nameof(e.RecruitmentGrade), "Enter the grade, for example BPS-14.");
            e.DepartmentId = null; e.PostId = null;
        }

        if (e.IsReusable && (e.Audience != ExamAudience.Public || e.Purpose != ExamPurpose.Practice))
            ModelState.AddModelError(nameof(e.IsReusable), "Repeat attempts are available only for public practice tests.");
        e.MaximumAttempts = 1;
        if (!e.RequireCamera) ModelState.AddModelError(nameof(e.RequireCamera), "Camera monitoring is required for every examination.");
        if (!e.RequireMicrophone) ModelState.AddModelError(nameof(e.RequireMicrophone), "Microphone monitoring is required for every examination.");
        if (!e.RequireFullscreen) ModelState.AddModelError(nameof(e.RequireFullscreen), "Fullscreen is required where the candidate browser supports it.");
    }

    private static void NormalizeTargeting(Exam exam)
    {
        exam.ExamTitle = exam.ExamTitle.Trim();
        exam.ClassName = string.IsNullOrWhiteSpace(exam.ClassName) ? null : exam.ClassName.Trim();
        exam.AcademicLevel = string.IsNullOrWhiteSpace(exam.AcademicLevel) ? null : exam.AcademicLevel.Trim();

        if (exam.Purpose != ExamPurpose.Recruitment)
        {
            exam.DepartmentId = null;
            exam.PostId = null;
        }

        if (exam.Purpose != ExamPurpose.AcademicClass)
            exam.AcademicLevel = null;

        exam.MaximumAttempts = 1;
        if (!exam.IsReusable) return;

        exam.Audience = ExamAudience.Public;
        exam.Purpose = ExamPurpose.Practice;
        exam.ExamDate = DateTime.Today;
        exam.StartTime = TimeSpan.Zero;
        exam.EntryWindowMinutes = 1440;
    }

    private static void RedistributeExamMarks(List<ExamQuestion> questions, decimal totalMarks, decimal negativeMarks)
    {
        if (questions.Count == 0) return;
        if (totalMarks < questions.Count * 0.01m)
            throw new InvalidOperationException("Total Marks are too small for the selected question count.");
        var totalUnits = (long)Math.Round(totalMarks * 100m, MidpointRounding.AwayFromZero);
        var baseUnits = totalUnits / questions.Count;
        var remainder = totalUnits % questions.Count;
        for (var i = 0; i < questions.Count; i++)
        {
            questions[i].Marks = (baseUnits + (i < remainder ? 1 : 0)) / 100m;
            questions[i].NegativeMarks = negativeMarks;
            questions[i].DisplayOrder = i + 1;
        }
    }

    private async Task Load(int? dep = null, int? post = null)
    {
        ViewBag.Departments = new SelectList(await _db.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DepartmentName).ToListAsync(), "DepartmentId", "DepartmentName", dep);
        var posts = await _db.Posts.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.PostName).ToListAsync();
        ViewBag.Posts = new SelectList(posts, "PostId", "PostName", post);
        ViewBag.PostRecords = posts;
        ViewBag.Classes = await _db.AcademicClasses.AsNoTracking()
            .Where(c => c.TeacherId == TeacherId && c.IsActive).OrderBy(c => c.Name).ToListAsync();
    }
}
