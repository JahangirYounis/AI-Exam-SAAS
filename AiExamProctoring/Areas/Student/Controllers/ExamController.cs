using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels.Student;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.Services.BehaviorAnalysis;
using AiExamProctoring.Services.ObjectDetection;
using AiExamProctoring.Services.SpeechRecognition;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AiExamProctoring.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Roles = "Student")]
public partial class ExamController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IProctoringStateService _states;
    private readonly ProctoringOptions _proctoringOptions;
    private readonly IProctoringEvidenceStore _evidenceStore;
    private readonly IWebHostEnvironment _environment;
    private readonly IFaceIdentityClient _faceIdentity;
    private readonly FaceIdentityOptions _faceOptions;
    private readonly IProctoringVerificationTicketService _verificationTickets;
    private readonly IBehaviorAnalysisClient _behaviorAnalysis;
    private readonly BehaviorAnalysisOptions _behaviorOptions;
    private readonly IObjectDetectionClient _objectDetection;
    private readonly ObjectDetectionOptions _objectOptions;
    private readonly ISpeechRecognitionClient _speechRecognition;
    private readonly SpeechRecognitionOptions _speechOptions;

    public ExamController(
        ApplicationDbContext context,
        IOptions<ProctoringOptions> proctoringOptions,
        IProctoringEvidenceStore evidenceStore,
        IWebHostEnvironment environment,
        IFaceIdentityClient faceIdentity,
        IOptions<FaceIdentityOptions> faceOptions,
        IProctoringVerificationTicketService verificationTickets,
        IBehaviorAnalysisClient behaviorAnalysis,
        IOptions<BehaviorAnalysisOptions> behaviorOptions,
        IObjectDetectionClient objectDetection,
        IOptions<ObjectDetectionOptions> objectOptions,
        ISpeechRecognitionClient speechRecognition,
        IOptions<SpeechRecognitionOptions> speechOptions,
        IProctoringStateService states)
    {
        _states = states;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _proctoringOptions = proctoringOptions?.Value
            ?? throw new ArgumentNullException(nameof(proctoringOptions));
        _evidenceStore = evidenceStore
            ?? throw new ArgumentNullException(nameof(evidenceStore));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _faceIdentity = faceIdentity ?? throw new ArgumentNullException(nameof(faceIdentity));
        _faceOptions = faceOptions?.Value ?? throw new ArgumentNullException(nameof(faceOptions));
        _verificationTickets = verificationTickets ?? throw new ArgumentNullException(nameof(verificationTickets));
        _behaviorAnalysis = behaviorAnalysis ?? throw new ArgumentNullException(nameof(behaviorAnalysis));
        _behaviorOptions = behaviorOptions?.Value ?? throw new ArgumentNullException(nameof(behaviorOptions));
        _objectDetection = objectDetection ?? throw new ArgumentNullException(nameof(objectDetection));
        _objectOptions = objectOptions?.Value ?? throw new ArgumentNullException(nameof(objectOptions));
        _speechRecognition = speechRecognition ?? throw new ArgumentNullException(nameof(speechRecognition));
        _speechOptions = speechOptions?.Value ?? throw new ArgumentNullException(nameof(speechOptions));
    }

    private string? StudentId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool InstructionsAccepted(int examId) =>
        _verificationTickets.TryValidate(Request.Cookies[$"exam-instructions-{examId}"], out var ticket) &&
        ticket!.StudentId == StudentId && ticket.ExamId == examId && ticket.Nonce == "instructions-v1";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptInstructions(int examId, bool understood)
    {
        if (StudentId is null) return Challenge();
        if (!understood) return BadRequest("Please read the instructions and confirm that you understand them.");
        // Joining a class and reading its instructions are allowed before release.
        // Publication is checked separately when verifying identity and starting.
        var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId);
        if (exam is null) return NotFound();
        if (exam.Audience == ExamAudience.Private && !await _context.ExamAssignments.AnyAsync(x => x.ExamId == examId && x.StudentId == StudentId && x.IsActive)) return Forbid();
        if (exam.Status == ExamStatus.Cancelled || (!exam.IsReusable && exam.Status == ExamStatus.Completed))
            return RedirectToAction(nameof(Available));
        var now = DateTime.UtcNow;
        Response.Cookies.Append($"exam-instructions-{examId}", _verificationTickets.Create(new(
            StudentId, examId, 0, "Instructions-v1", now, now.AddHours(2), "instructions-v1")),
            new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/Student/Exam", MaxAge = TimeSpan.FromHours(2), IsEssential = true });
        return RedirectToAction(nameof(PreCheck), new { examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseOwnAttempt(int attemptId)
    {
        if (StudentId is null) return Unauthorized();
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var attempt = await _context.ExamAttempts.FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.StudentId == StudentId);
            if (attempt is null) return NotFound();
            if (attempt.Status != AttemptStatus.InProgress) return Conflict("This attempt is no longer active.");
            var session = await _context.ExamSessionAttempts.Where(x => x.AttemptId == attemptId && x.Status == AttemptStatus.InProgress)
                .OrderByDescending(x => x.SessionAttemptId).FirstOrDefaultAsync();
            if (session is null) return Conflict("There is no active session.");
            attempt.Status = session.Status = AttemptStatus.ProctoringBlocked;
            _context.ProctoringLogs.Add(new ProctoringLog
            {
                StudentId = StudentId,
                ExamId = attempt.ExamId,
                AttemptId = attemptId,
                SessionAttemptId = session.SessionAttemptId,
                AlertType = ProctoringAlertType.ExamBlocked,
                Description = "Candidate requested a pause. Saved answers are kept; teacher release is required to resume.",
                ModelName = "CandidateRequestedPause",
                CountsAsViolation = false,
                IsReviewed = true,
                ClientEventId = Guid.NewGuid().ToString("N"),
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return RedirectToAction(nameof(Blocked), new { attemptId });
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reminder(int logId) => Task.FromResult<IActionResult>(
        StatusCode(StatusCodes.Status410Gone, new { error = "Legacy reminders retired. Use the authoritative proctoring state." }));

    [HttpGet]
    public async Task<IActionResult> Join(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 40)
        {
            TempData["Error"] = "Enter the test code your teacher shared.";
            return RedirectToAction("Index", "Dashboard");
        }
        var matches = await _context.Exams.AsNoTracking()
            .Where(x => x.ClassCode == normalized && x.Audience == ExamAudience.Private &&
                x.Status != ExamStatus.Cancelled && x.Status != ExamStatus.Completed)
            .Take(2).ToListAsync();
        if (matches.Count != 1)
        {
            TempData["Error"] = "This code is unavailable. Ask your teacher for the current test link.";
            return RedirectToAction("Index", "Dashboard");
        }
        return View(matches[0]);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinConfirmed(string code)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            if (StudentId is null) return Challenge();
            var normalized = code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized) || normalized.Length > 40) return BadRequest();
            // Serialize admission so double clicks cannot create duplicate assignments.
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var exams = await _context.Exams.Where(x => x.ClassCode == normalized &&
                x.Audience == ExamAudience.Private && x.Status != ExamStatus.Cancelled &&
                x.Status != ExamStatus.Completed).Take(2).ToListAsync();
            if (exams.Count != 1) return NotFound();
            var exam = exams[0];
            var student = await _context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == StudentId);
            if (student is null || student.IsBlocked || !student.IsApproved) return Forbid();
            var assignment = await _context.ExamAssignments.FirstOrDefaultAsync(x => x.ExamId == exam.ExamId && x.StudentId == StudentId);
            if (assignment is { IsActive: false })
            {
                TempData["Error"] = "Your teacher has removed your access. Contact them to rejoin.";
                return RedirectToAction("Index", "Dashboard");
            }
            if (assignment is null && !(exam.AcademicClassId.HasValue && await _context.ClassMemberships.AnyAsync(m =>
                m.AcademicClassId == exam.AcademicClassId && m.StudentId == StudentId && m.IsActive && m.AcademicClass!.IsActive)))
            {
                TempData["Error"] = "This test is only for the teacher-approved class roster. A shared code does not grant access. Contact your teacher.";
                return RedirectToAction("Index", "Dashboard");
            }
            if (exam.AcademicClassId.HasValue && !await _context.ClassMemberships.AnyAsync(m => m.AcademicClassId == exam.AcademicClassId && m.StudentId == StudentId && m.IsActive && m.AcademicClass!.IsActive)) return Forbid();
            if (assignment is null)
            {
                _context.ExamAssignments.Add(new ExamAssignment
                {
                    ExamId = exam.ExamId,
                    StudentId = StudentId,
                    AssignedBy = StudentId,
                    RollNumber = $"E{exam.ExamId}-{Guid.NewGuid():N}"[..35],
                    IsActive = true
                });
                await _context.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            TempData["Success"] = $"You joined {exam.ExamTitle}. Your identity will be checked before you can start.";
            return RedirectToAction(nameof(PreCheck), new { examId = exam.ExamId });

        });
    }

    [HttpGet]
    [Route("/Student/Exam/Available")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Available()
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Challenge();

        var cancellationToken = HttpContext.RequestAborted;
        var now = DateTime.Now;
        ViewBag.ExamServerNow = now;
        ViewBag.ExamRollNumbers = new Dictionary<int, string>();
        ViewBag.ResumableAttempts = new List<ExamAttempt>();
        ViewBag.ExamLoadError = null;

        try
        {
            var exams = await _context.Exams.AsNoTracking()
                .Include(exam => exam.Department)
                .Include(exam => exam.Post)
                .Where(exam => exam.Status != ExamStatus.Cancelled &&
                    ((exam.Audience == ExamAudience.Public && exam.IsPublished) ||
                     exam.Assignments.Any(assignment => assignment.StudentId == studentId && assignment.IsActive)))
                .Where(exam => !exam.AcademicClassId.HasValue ||
                    _context.ClassMemberships.Any(member =>
                        member.AcademicClassId == exam.AcademicClassId &&
                        member.StudentId == studentId && member.IsActive && member.AcademicClass!.IsActive))
                .OrderBy(exam => exam.ExamDate)
                .ThenBy(exam => exam.StartTime)
                .ThenBy(exam => exam.ExamId)
                .ToListAsync(cancellationToken);

            var assignments = await _context.ExamAssignments.AsNoTracking()
                .Where(assignment => assignment.StudentId == studentId && assignment.IsActive && assignment.RollNumber != null)
                .Select(assignment => new { assignment.ExamId, assignment.RollNumber, assignment.AssignedAt })
                .ToListAsync(cancellationToken);
            ViewBag.ExamRollNumbers = assignments.GroupBy(assignment => assignment.ExamId)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(assignment => assignment.AssignedAt).First().RollNumber!);

            var attemptedIds = (await _context.ExamAttempts.AsNoTracking()
                .Where(attempt => attempt.StudentId == studentId)
                .Select(attempt => attempt.ExamId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

            ViewBag.ResumableAttempts = await _context.ExamAttempts.AsNoTracking()
                .Include(attempt => attempt.Exam)
                .Where(attempt => attempt.StudentId == studentId &&
                    (attempt.Status == AttemptStatus.InProgress || attempt.Status == AttemptStatus.ProctoringBlocked))
                .OrderByDescending(attempt => attempt.StartedAt)
                .ToListAsync(cancellationToken);

            var available = exams.Where(exam =>
                (exam.AllowsRepeatAttempts || !attemptedIds.Contains(exam.ExamId)) &&
                exam.Status != ExamStatus.Completed &&
                (exam.IsReusable || now <= exam.LatestStartDate)).ToList();

            return View("Available", available);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var logger = HttpContext.RequestServices
                .GetService(typeof(Microsoft.Extensions.Logging.ILogger<ExamController>))
                as Microsoft.Extensions.Logging.ILogger<ExamController>;
            if (logger is not null)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, error,
                    "Available examinations failed for student {StudentId}; reference {Reference}",
                    studentId, HttpContext.TraceIdentifier);
            }
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            ViewBag.ExamLoadError = "Please retry or contact your teacher. Support reference: " + HttpContext.TraceIdentifier;
            return View("Available", new List<Exam>());
        }
    }


    [HttpGet]
    public async Task<IActionResult> Past(CancellationToken cancellationToken)
    {
        if (StudentId is null) return Challenge();
        var now = DateTime.Now;
        var exams = await _context.Exams.AsNoTracking()
            .Where(x => x.Attempts.Any(a => a.StudentId == StudentId) ||
                x.Assignments.Any(a => a.StudentId == StudentId && a.IsActive) ||
                (x.Audience == ExamAudience.Public && x.IsPublished))
            .OrderByDescending(x => x.ExamDate).ToListAsync(cancellationToken);
        ViewBag.Attempts = await _context.ExamAttempts.AsNoTracking()
            .Where(x => x.StudentId == StudentId).OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
        return View(exams.Where(x => !x.IsReusable &&
            (now > x.LatestStartDate || x.Status == ExamStatus.Completed || x.Status == ExamStatus.Cancelled)).ToList());
    }

    // Replace only the existing ReleaseStatus action with this action.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReleaseStatus(int examId)
    {
        var studentId = StudentId;
        if (string.IsNullOrWhiteSpace(studentId)) return Unauthorized();
        if (examId <= 0) return BadRequest();
        var ct = HttpContext.RequestAborted;
        var exam = await _context.Exams.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == examId, ct);
        if (exam is null) return NotFound();
        if (exam.Audience == ExamAudience.Private &&
            !await _context.ExamAssignments.AsNoTracking().AnyAsync(
                x => x.ExamId == examId && x.StudentId == studentId && x.IsActive, ct))
            return Forbid();
        return Json(new
        {
            released = exam.IsPublished,
            reusable = exam.IsReusable,
            terminal = exam.Status == ExamStatus.Cancelled ||
                (!exam.IsReusable && exam.Status == ExamStatus.Completed),
            serverNow = DateTime.UtcNow,
            start = exam.IsReusable ? (DateTime?)null : exam.StartDate.ToUniversalTime(),
            latest = exam.IsReusable ? (DateTime?)null : exam.LatestStartDate.ToUniversalTime()
        });
    }

    [HttpGet]
    [Route("/Student/Exam/PreCheck")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PreCheck(int examId)
    {
        if (StudentId is null) return Challenge();
        var exam = await _context.Exams.AsNoTracking()
            .Include(x => x.Department)
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.ExamId == examId);
        if (exam is null) return NotFound();
        var assignment = await _context.ExamAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId && x.StudentId == StudentId && x.IsActive);
        if (exam.Audience == ExamAudience.Private && assignment is null) return Forbid();
        ViewBag.RollNumber = assignment?.RollNumber;
        if (exam.Status == ExamStatus.Cancelled || (!exam.IsReusable && exam.Status == ExamStatus.Completed))
            return RedirectToAction(nameof(Available));

        var activeAttemptId = await _context.ExamAttempts.AsNoTracking()
            .Where(x => x.ExamId == examId && x.StudentId == StudentId &&
                        (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.ProctoringBlocked))
            .Select(x => (int?)x.AttemptId)
            .FirstOrDefaultAsync();
        if (activeAttemptId.HasValue)
            return RedirectToAction(nameof(Resume), new { attemptId = activeAttemptId.Value });

        var hasPreviousAttempt = await _context.ExamAttempts.AsNoTracking()
            .AnyAsync(x => x.ExamId == examId && x.StudentId == StudentId);
        if (hasPreviousAttempt && !exam.AllowsRepeatAttempts)
        {
            TempData["Error"] = "You have already used your one allowed attempt for this examination.";
            return RedirectToAction(nameof(Available));
        }

        ViewBag.IsReleased = exam.IsPublished;
        if (!exam.IsReusable && DateTime.Now > exam.LatestStartDate)
        {
            TempData["Error"] = $"The entry window for this examination closed at {exam.LatestStartDate:dd MMM yyyy, hh:mm tt}.";
            return RedirectToAction(nameof(Available));
        }

        if (!InstructionsAccepted(examId)) return View("Instructions", exam);

        var unread = await _context.StudentNotifications
            .Where(x => x.StudentId == StudentId && x.ExamId == examId && !x.IsRead)
            .ToListAsync();
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadDate = DateTime.Now;
        }
        if (unread.Count > 0) await _context.SaveChangesAsync();

        var student = await _context.ApplicationUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == StudentId);
        if (student is null)
            return Challenge();

        if (!HasStoredProfilePhoto(student))
        {
            TempData["Error"] = "Before starting an examination, upload a clear and recent profile photo, then complete Model 1 face enrollment.";
            var returnUrl = Url.Action(nameof(PreCheck), "Exam", new { area = "Student", examId });
            return RedirectToAction("Edit", "Profile", new { area = "Student", returnUrl });
        }

        var templateStatus = await _faceIdentity.TemplateStatusAsync(StudentId, HttpContext.RequestAborted);
        if (templateStatus.Success && templateStatus.Data?.Exists != true)
        {
            TempData["Error"] = "Create your multi-photo Face Identity template before starting an examination.";
            var returnUrl = Url.Action(nameof(PreCheck), "Exam", new { area = "Student", examId });
            return RedirectToAction("FaceIdentity", "Profile", new { area = "Student", returnUrl });
        }
        ViewBag.FaceIdentityReady = templateStatus.Success && templateStatus.Data?.Exists == true;
        ViewBag.FaceIdentityMessage = templateStatus.Success ? null : templateStatus.Message;
        ViewBag.FaceVerificationFrames = _faceOptions.VerificationFrames;

        // Device readiness needs identity enrollment only. Optional monitoring services
        // are initialized by the existing exam-session pipeline.
        ViewBag.ServerNow = DateTime.Now;
        return View(exam);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/VerifyIdentity")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> VerifyIdentity([FromForm] FaceIdentityFrameRequest request)
    {
        if (StudentId is null)
            return Unauthorized();

        if (!InstructionsAccepted(request.ExamId))
            return StatusCode(428, new { ok = false, message = "Read and accept the exam instructions before verification." });

        if (request.ExamId <= 0 || request.Frames.Count != _faceOptions.VerificationFrames)
            return BadRequest(new { ok = false, message = $"Exactly {_faceOptions.VerificationFrames} fresh camera frames are required." });

        var exam = await _context.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == request.ExamId);
        if (exam is null) return NotFound(new { message = "This examination no longer exists. Return to examinations." });
        if (exam.Audience == ExamAudience.Private && !await _context.ExamAssignments.AsNoTracking().AnyAsync(x => x.ExamId == request.ExamId && x.StudentId == StudentId && x.IsActive))
            return StatusCode(403, new { message = "You are not assigned to this examination." });
        var images = await ReadFaceFramesAsync(request.Frames, HttpContext.RequestAborted);
        if (images.Count != _faceOptions.VerificationFrames)
            return BadRequest(new { ok = false, message = "One or more camera frames were invalid." });
        var result = await _faceIdentity.VerifyAsync(StudentId, images, HttpContext.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            var statusCode = result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(statusCode, new { ok = false, message = IdentityMessage.For(result.Data?.ReasonCode, result.Message), status = result.Data?.Status, reasonCode = result.Data?.ReasonCode });
        }
        if (!result.Data.Verified)
            return StatusCode(result.Data.Status == "retry" ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status403Forbidden,
                new { ok = false, message = IdentityMessage.For(result.Data.ReasonCode, result.Data.Message ?? result.Message), reasonCode = result.Data.ReasonCode, status = result.Data.Status, score = result.Data.MedianSimilarity, threshold = result.Data.Threshold });

        // Entry verifies identity only. Behavior calibration starts inside the exam.
        var verifiedAt = DateTime.UtcNow;
        var ticket = _verificationTickets.Create(new ProctoringVerificationTicket(
            StudentId,
            request.ExamId,
            result.Data.MedianSimilarity,
            result.Data.ModuleVersion ?? _faceOptions.ModelVersion,
            verifiedAt,
            verifiedAt.AddMinutes(Math.Clamp(_faceOptions.VerificationTicketMinutes, 1, 15)),
            Guid.NewGuid().ToString("N")));
        return Json(new
        {
            ok = true,
            verified = true,
            ticket,
            score = result.Data.MedianSimilarity,
            threshold = result.Data.Threshold,
            voteRatio = result.Data.VoteRatio,
            modelVersion = result.Data.ModuleVersion
        });
    }
    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/Start")]
    public async Task<IActionResult> Start(int examId, string? verificationTicket)
    {
        if (StudentId is null) return Challenge();
        if (!InstructionsAccepted(examId)) return RedirectToAction(nameof(PreCheck), new { examId });
        var student = await _context.ApplicationUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == StudentId);
        if (student is null) return Challenge();
        if (student.IsBlocked) { TempData["Error"] = "Your student account is blocked. Please contact the administrator."; return RedirectToAction(nameof(Available)); }
        if (!student.IsApproved) { TempData["Error"] = "Your account is awaiting administrator approval."; return RedirectToAction(nameof(Available)); }
        if (!HasStoredProfilePhoto(student))
        {
            TempData["Error"] = "A clear, recent profile photo is required before every first exam entry. It is kept with your candidate record and appears on your certificate.";
            var returnUrl = Url.Action(nameof(PreCheck), "Exam", new { area = "Student", examId });
            return RedirectToAction("Edit", "Profile", new { area = "Student", returnUrl });
        }
        if (!_verificationTickets.TryValidate(verificationTicket, out var verified) ||
            verified is null || verified.StudentId != StudentId || verified.ExamId != examId ||
            verified.Nonce.StartsWith("session:", StringComparison.Ordinal) || verified.Nonce == "instructions-v1" ||
            verified.VerifiedAtUtc < DateTime.UtcNow.AddSeconds(-30))
        {
            TempData["Error"] = "Complete the live Face Identity verification immediately before starting the examination.";
            return RedirectToAction(nameof(PreCheck), new { examId });
        }
        var exam = await _context.Exams
            .Include(x => x.ExamQuestions)
            .Include(x => x.Sessions).ThenInclude(x => x.ExamQuestions)
            .FirstOrDefaultAsync(x => x.ExamId == examId && x.IsPublished &&
                x.Status != ExamStatus.Cancelled && (x.IsReusable || x.Status != ExamStatus.Completed));
        if (exam is null) return NotFound();
        var hasAssignment = await _context.ExamAssignments.AsNoTracking()
            .AnyAsync(x => x.ExamId == examId && x.StudentId == StudentId && x.IsActive);
        if (!hasAssignment && exam.Audience == ExamAudience.Private) return Forbid();
        if (!hasAssignment && exam.Audience == ExamAudience.Public)
            await EnsurePublicAssignmentAsync(exam, StudentId);
        var now = DateTime.Now;
        if (!exam.IsReusable && now < exam.StartDate)
        {
            TempData["Error"] = $"This examination starts at {exam.StartDate:dd MMM yyyy, hh:mm tt}. You cannot start it early.";
            return RedirectToAction(nameof(PreCheck), new { examId });
        }
        if (!exam.IsReusable && now > exam.LatestStartDate)
        {
            TempData["Error"] = $"The entry window closed at {exam.LatestStartDate:dd MMM yyyy, hh:mm tt}.";
            return RedirectToAction(nameof(Available));
        }
        if (!exam.IsReadyForScheduling) { TempData["Error"] = "This examination is not fully configured by its teacher yet."; return RedirectToAction(nameof(Available)); }
        var existingAttempts = await _context.ExamAttempts.AsNoTracking()
            .Where(x => x.StudentId == StudentId && x.ExamId == examId)
            .Select(x => new { x.AttemptId, x.Status })
            .ToListAsync();
        var activeAttempt = existingAttempts.FirstOrDefault(x =>
            x.Status is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked);
        if (activeAttempt is not null)
            return RedirectToAction(nameof(Resume), new { attemptId = activeAttempt.AttemptId });
        if (existingAttempts.Count > 0 && !exam.AllowsRepeatAttempts)
        {
            TempData["Error"] = "You have already used your one allowed attempt for this examination.";
            return RedirectToAction(nameof(Available));
        }

        var first = exam.Sessions.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).FirstOrDefault();
        if (first is null) { TempData["Error"] = "No active examination session is configured."; return RedirectToAction(nameof(Available)); }

        // SQL Server retry is enabled in Program.cs. Explicit transactions must run
        // through CreateExecutionStrategy(), otherwise EF Core throws
        // SqlServerRetryingExecutionStrategy does not support user-initiated transactions.
        var strategy = _context.Database.CreateExecutionStrategy();
        var createdSessionAttemptId = 0;
        var studentId = StudentId;
        var galleryStatus = await _faceIdentity.TemplateStatusAsync(studentId!, HttpContext.RequestAborted);
        var enrolledPhoto = galleryStatus.Success && galleryStatus.Data?.Exists == true
            ? await _faceIdentity.EnrollmentPhotoAsync(studentId!, galleryStatus.Data.BestPhotoIndex, HttpContext.RequestAborted) : null;
        if (enrolledPhoto?.Bytes is not { Length: > 0 and <= 5000000 }) {
            TempData["Error"] = "Your saved identity photo could not be loaded. Please retry or ask your teacher to check your enrollment.";
            return RedirectToAction(nameof(PreCheck), new { examId });
        }
        var attemptPhoto = enrolledPhoto.Bytes;
        var entryLogId = 0;
        var duplicateAttemptDetected = false;
        var firstSessionId = first.SessionId;
        var firstSessionMarks = first.TotalMarks;
        var examTotalMarks = exam.TotalMarks;

        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            // Serializable range locking prevents two near-simultaneous Start
            // requests from creating parallel attempts. Reusable public practice
            // permits another completed attempt, never two active attempts.
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var transactionAttempts = await _context.ExamAttempts
                .Where(x => x.StudentId == studentId && x.ExamId == examId)
                .Select(x => x.Status)
                .ToListAsync();
            if (transactionAttempts.Any(s => s is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked)
                || (transactionAttempts.Count > 0 && !exam.AllowsRepeatAttempts))
            {
                duplicateAttemptDetected = true;
                await tx.RollbackAsync();
                return;
            }

            var attempt = new ExamAttempt
            {
                StudentId = studentId!,
                ExamId = examId,
                StartedAt = DateTime.Now,
                Status = AttemptStatus.InProgress,
                TotalMarks = examTotalMarks
            };

            _context.ExamAttempts.Add(attempt);
            await _context.SaveChangesAsync();

            var sessionAttempt = new ExamSessionAttempt
            {
                AttemptId = attempt.AttemptId,
                SessionId = firstSessionId,
                StartedAt = DateTime.Now,
                Status = AttemptStatus.InProgress,
                TotalMarks = firstSessionMarks
            };

            _context.ExamSessionAttempts.Add(sessionAttempt);
            await _context.SaveChangesAsync();
            createdSessionAttemptId = sessionAttempt.SessionAttemptId;
            _context.AttemptIdentityPhotos.Add(new AttemptIdentityPhoto { AttemptId=attempt.AttemptId,
                ImageBytes=attemptPhoto, Sha256=Convert.ToHexString(SHA256.HashData(attemptPhoto)), CapturedAtUtc=DateTime.UtcNow });
            await _context.SaveChangesAsync();


            var identityLog = new ProctoringLog
            {
                StudentId = studentId!,
                ExamId = examId,
                AttemptId = attempt.AttemptId,
                SessionAttemptId = sessionAttempt.SessionAttemptId,
                AlertType = ProctoringAlertType.IdentityVerified,
                CreatedDate = DateTime.Now,
                CountsAsViolation = false,
                IsReviewed = true,
                Description = "Fresh precheck identity transferred to this new exam session.",
                ConfidenceScore = verified.IdentityScore,
                ModelName = _faceOptions.ModelName,
                ModelVersion = verified.ModelVersion,
                ClientEventId = Guid.NewGuid().ToString("N")
            };
            _context.ProctoringLogs.Add(identityLog);
            await _context.SaveChangesAsync();
            entryLogId = identityLog.LogId;

            await tx.CommitAsync();
        });

        if (duplicateAttemptDetected || createdSessionAttemptId <= 0)
        {
            TempData["Error"] = "The examination attempt already exists.";
            return RedirectToAction(nameof(Available));
        }

        Response.Cookies.Delete($"exam-instructions-{examId}", new CookieOptions { Path = "/Student/Exam" });
        var entryTicket = _verificationTickets.Create(verified with
        {
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30),
            Nonce = $"session:{createdSessionAttemptId}:{entryLogId}"
        });
        return RedirectToAction(nameof(Session), new { sessionAttemptId = createdSessionAttemptId, entryTicket });
    }

    [HttpGet]
    [Route("/Student/Exam/Resume")]
    public async Task<IActionResult> Resume(int attemptId)
    {
        if (StudentId is null)
            return Challenge();

        var attempt = await _context.ExamAttempts
            .Include(x => x.Exam)
            .Include(x => x.Student)
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.StudentId == StudentId);

        if (attempt is null)
            return NotFound();

        if (attempt.Status == AttemptStatus.ProctoringBlocked)
        {
            return RedirectToAction(nameof(Blocked), new { attemptId });
        }

        // If attempt is already completed, show result
        if (attempt.Status != AttemptStatus.InProgress)
        {
            return RedirectToAction(
                nameof(AttemptResult),
                new { attemptId });
        }

        // Check whether there is already an active session.
        // IMPORTANT: only declare 'active' once.
        var active = await _context.ExamSessionAttempts
            .Include(x => x.Session)
            .Where(x =>
                x.AttemptId == attemptId &&
                x.Status == AttemptStatus.InProgress)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync();

        // Resume the existing active session
        if (active is not null)
        {
            return RedirectToAction(
                nameof(Session),
                new { sessionAttemptId = active.SessionAttemptId });
        }

        // No active session exists. Continue() owns next-session and rest logic so
        // Resume cannot accidentally bypass the server-side break.
        return RedirectToAction(nameof(Continue), new { attemptId });
    }

    [HttpGet]
    [Route("/Student/Exam/Session")]
    public async Task<IActionResult> Session(int sessionAttemptId, string? entryTicket)
    {
        if (StudentId is null) return Challenge();
        await RestoreReleasedWaitingTimeAsync(sessionAttemptId);
        var sa = await _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Session).ThenInclude(x => x!.SessionSubjects).ThenInclude(x => x!.Subject)
            .FirstOrDefaultAsync(x => x.SessionAttemptId == sessionAttemptId && x.Attempt!.StudentId == StudentId);
        if (sa?.Attempt?.Exam is null || sa.Session is null) return NotFound();
        if (sa.Attempt.Status == AttemptStatus.ProctoringBlocked ||
            sa.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId = sa.AttemptId });
        if (sa.Status != AttemptStatus.InProgress) return RedirectToAction(nameof(Continue), new { attemptId = sa.AttemptId });

        if (DateTime.Now >= (sa.StartedAt ?? DateTime.Now).AddMinutes(sa.Session.DurationMinutes))
        {
            await FinalizeSession(sa, await GetSessionQuestionsAsync(sa), true);
            return RedirectToAction(nameof(Continue), new { attemptId = sa.AttemptId });
        }

        // Precheck approval does not grant access to the paper. A new,
        // session-bound verification is required on every entry or resume.
        if (!_verificationTickets.TryValidate(entryTicket, out var entry) ||
            entry!.StudentId != StudentId || entry.ExamId != sa.Attempt.ExamId ||
            !entry.Nonce.StartsWith($"session:{sessionAttemptId}:", StringComparison.Ordinal) ||
            !await HasRecentIdentityAsync(sessionAttemptId, entry.Nonce))
        {
            return View("IdentityEntry", sa);
        }

        var exam = sa.Attempt.Exam;
        var questions = await GetSessionQuestionsAsync(sa);
        if (questions.Count == 0) { TempData["Error"] = "This session has no active questions configured."; return RedirectToAction(nameof(Available)); }
        var resumeQuestionId = sa.LastQuestionId.HasValue && questions.Any(x => x.QuestionId == sa.LastQuestionId.Value)
            ? sa.LastQuestionId.Value
            : questions[0].QuestionId;
        var existing = await _context.StudentAnswers.AsNoTracking().Where(a => a.SessionAttemptId == sessionAttemptId)
            .ToDictionaryAsync(a => a.QuestionId, a => a.SelectedOption);
        var reviewed = await _context.StudentAnswers.AsNoTracking().Where(a => a.SessionAttemptId == sessionAttemptId && a.IsMarkedForReview)
            .Select(a => a.QuestionId).ToListAsync();
        var sessionIndex = await _context.ExamSessions.Where(x => x.ExamId == exam.ExamId && x.IsActive && x.DisplayOrder <= sa.Session.DisplayOrder).CountAsync();
        // Authoritative session time is server-side and starts only when this
        // session attempt is created. Rest between sessions never consumes it.
        var expires = (sa.StartedAt ?? DateTime.Now).AddMinutes(sa.Session.DurationMinutes);
        if (DateTime.Now >= expires)
        {
            await FinalizeSession(sa, questions, true);
            return RedirectToAction(nameof(Continue), new { attemptId = sa.AttemptId });
        }
        ViewBag.ResumeQuestionId = resumeQuestionId;
        var objectHealth = await _objectDetection.HealthAsync(HttpContext.RequestAborted);
        ViewBag.ObjectDetectionReady = objectHealth.Success && objectHealth.Data?.Ready == true;
        ViewBag.ObjectDetectionEnabled = _objectOptions.Enabled;
        ViewBag.ObjectDetectionMessage = objectHealth.Success
            ? objectHealth.Data?.Message ?? "Model 3 is ready."
            : objectHealth.Message;
        var speechHealth = await _speechRecognition.HealthAsync(HttpContext.RequestAborted);
        ViewBag.SpeechRecognitionReady = speechHealth.Success && speechHealth.Data?.Ready == true;
        ViewBag.SpeechRecognitionMessage = speechHealth.Success
            ? speechHealth.Data?.Message ?? "Model 4 is ready."
            : speechHealth.Message;
        return View("Attempt", new SessionAttemptViewModel { SessionAttempt = sa, Exam = exam, Session = sa.Session, Questions = questions, ExistingAnswers = existing, ReviewedQuestionIds = reviewed.ToHashSet(), SessionIndex = sessionIndex, TotalSessions = exam.NumberOfSessions, ExpiresAt = expires, ServerNow = DateTime.Now });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/SubmitSession")]
    public async Task<IActionResult> SubmitSession()
    {
        if (StudentId is null) return Challenge();
        int? submittedSessionId = null;
        for (var retry = 0; ; retry++)
        {
            try
            {
                return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    var form = await Request.ReadFormAsync();
                    int? attemptId = null;
                    if (int.TryParse(form["sessionAttemptId"], out var sessionId))
                    {
                        submittedSessionId = sessionId;
                        attemptId = await _context.ExamSessionAttempts.AsNoTracking()
                            .Where(s => s.SessionAttemptId == sessionId && s.Attempt!.StudentId == StudentId)
                            .Select(s => (int?)s.AttemptId).FirstOrDefaultAsync();
                    }
                    // Acquire the attempt update lock before reading mutable session state,
                    // in the same order as teacher controls and the strike engine.
                    await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                    if (attemptId.HasValue)
                        await ProctoringStateService.LockAttemptAsync(_context, attemptId.Value, HttpContext.RequestAborted);
                    var response = await SubmitSessionCore();
                    await transaction.CommitAsync();
                    return response;
                });
            }
            catch (DbUpdateConcurrencyException) when (retry < 2)
            {
                // Autosave, timeout processing or a teacher hold may win the race.
                // Reload authoritative state; never force an outdated submission over it.
                _context.ChangeTracker.Clear();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _context.ChangeTracker.Clear();
                HttpContext.RequestServices.GetService<ILogger<ExamController>>()?
                    .LogError(error, "Exam submission failed for student {StudentId}; trace {TraceId}", StudentId, HttpContext.TraceIdentifier);
                try
                {
                    var directory = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "diagnostics");
                    Directory.CreateDirectory(directory);
                    await System.IO.File.WriteAllTextAsync(Path.Combine(directory, $"submission-{Guid.NewGuid():N}.log"),
                        $"UTC: {DateTime.UtcNow:O}\nReference: {HttpContext.TraceIdentifier}\n{error}");
                }
                catch (Exception diagnosticError) when (diagnosticError is not OperationCanceledException)
                {
                    HttpContext.RequestServices.GetService<ILogger<ExamController>>()?
                        .LogWarning(diagnosticError, "Could not persist private submission diagnostic");
                }
                if (submittedSessionId.HasValue)
                    ViewBag.SessionAttemptId = submittedSessionId.Value;
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                ViewBag.SubmissionMessage = "We could not confirm your submission. Your previously saved answers are kept. Return to your examination and try again, or contact your instructor.";
                return View("SubmissionProblem");
            }
        }
    }

    private async Task<IActionResult> SubmitSessionCore()
    {
        if (StudentId is null)
            return Challenge();

        // Do not bind the answer dictionary through MVC's DictionaryModelBinder.
        // A malformed/legacy form field can otherwise be interpreted as an integer
        // dictionary key (for example "sessionAttemptId") and throw FormatException
        // before this action executes. Parse the form defensively instead.
        var form = await Request.ReadFormAsync();

        if (!int.TryParse(form["sessionAttemptId"].FirstOrDefault(), out var sessionAttemptId) ||
            sessionAttemptId <= 0)
        {
            TempData["Error"] = "The examination session could not be identified. Please reopen your active examination.";
            return RedirectToAction(nameof(Available));
        }

        var autoSubmitted = bool.TryParse(
            form["autoSubmitted"].FirstOrDefault(),
            out var postedAutoSubmit) && postedAutoSubmit;

        var answers = new Dictionary<int, string?>();

        foreach (var key in form.Keys)
        {
            if (!key.StartsWith("answers[", StringComparison.OrdinalIgnoreCase) ||
                !key.EndsWith("]", StringComparison.Ordinal))
            {
                continue;
            }

            var rawQuestionId = key[8..^1];
            if (!int.TryParse(rawQuestionId, out var questionId) || questionId <= 0)
            {
                continue;
            }

            var rawValue = form[key].FirstOrDefault();
            answers[questionId] = string.IsNullOrWhiteSpace(rawValue)
                ? null
                : rawValue.Trim().ToUpperInvariant();
        }

        var sa = await _context.ExamSessionAttempts
            .Include(x => x.Attempt)
                .ThenInclude(x => x!.Exam)
            .Include(x => x.Session)
                .ThenInclude(x => x!.SessionSubjects)
                .ThenInclude(x => x!.Subject)
            .FirstOrDefaultAsync(x =>
                x.SessionAttemptId == sessionAttemptId &&
                x.Attempt!.StudentId == StudentId);

        if (sa?.Attempt is null || sa.Session is null)
            return NotFound();

        if (sa.Attempt.Status == AttemptStatus.ProctoringBlocked ||
            sa.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId = sa.AttemptId });

        if (sa.Status != AttemptStatus.InProgress)
            return await NextSession(sa.AttemptId) is not null
                ? RedirectToAction(nameof(Break), new { attemptId = sa.AttemptId })
                : RedirectToAction(nameof(Completed), new { sessionAttemptId });

        var questions = await GetSessionQuestionsAsync(sa);
        if (questions.Count == 0)
        {
            TempData["Error"] = "This examination session does not contain any active questions.";
            return RedirectToAction(nameof(Available));
        }

        var expiry = (sa.StartedAt ?? DateTime.Now)
            .AddMinutes(sa.Session.DurationMinutes);

        if (sa.FrozenAtUtc.HasValue) return RedirectToAction(nameof(Session), new { sessionAttemptId });

        var serverAutoSubmit = DateTime.Now >= expiry;
        autoSubmitted = autoSubmitted || serverAutoSubmit;

        if (!serverAutoSubmit && !await HasRecentIdentityAsync(sessionAttemptId))
        {
            ViewBag.SessionAttemptId = sessionAttemptId;
            ViewBag.SubmissionMessage = "Your exam has not been submitted. A fresh identity check is required. Return to your examination, complete the camera check, then submit again. Your saved answers are kept.";
            return View("SubmissionProblem");
        }

        if (serverAutoSubmit)
        {
            // Session time is authoritative on the server. Once expired, ignore
            // the browser's final payload and grade only answers that reached the
            // database before the expiry instant.
            answers = await _context.StudentAnswers
                .AsNoTracking()
                .Where(a => a.SessionAttemptId == sessionAttemptId)
                .ToDictionaryAsync(a => a.QuestionId, a => a.SelectedOption);
        }

        var oldAnswers = await _context.StudentAnswers
            .Where(a => a.SessionAttemptId == sessionAttemptId)
            .ToListAsync();

        decimal marks = 0m;
        var correct = 0;
        var wrong = 0;
        var skipped = 0;
        var answeredAt = DateTime.Now;

        foreach (var question in questions)
        {
            answers.TryGetValue(question.QuestionId, out var selected);
            selected = string.IsNullOrWhiteSpace(selected)
                ? null
                : selected.Trim().ToUpperInvariant();

            if (selected is not null && !IsValidOption(question, selected))
                selected = null;

            var answer = oldAnswers.FirstOrDefault(a => a.QuestionId == question.QuestionId);
            if (answer is null)
            {
                answer = new StudentAnswer { SessionAttemptId = sessionAttemptId, QuestionId = question.QuestionId };
                _context.StudentAnswers.Add(answer);
            }
            answer.SelectedOption = selected;
            answer.AnsweredAt = answeredAt;

            if (selected is null)
            {
                answer.IsCorrect = null;
                answer.MarksAwarded = 0m;
                skipped++;
            }
            else if (string.Equals(
                         selected,
                         question.CorrectAnswer,
                         StringComparison.OrdinalIgnoreCase))
            {
                answer.IsCorrect = true;
                answer.MarksAwarded = question.ExamMarks;
                marks += question.ExamMarks;
                correct++;
            }
            else
            {
                answer.IsCorrect = false;
                answer.MarksAwarded = -question.ExamNegativeMarks;
                marks -= question.ExamNegativeMarks;
                wrong++;
            }

        }

        sa.MarksObtained = marks;
        sa.TotalMarks = questions.Sum(q => q.ExamMarks);
        sa.CorrectAnswers = correct;
        sa.WrongAnswers = wrong;
        sa.SkippedAnswers = skipped;

        await _context.SaveChangesAsync();
        await FinalizeSession(sa, questions, autoSubmitted);

        if (await NextSession(sa.AttemptId) is null)
        {
            await FinalizeAttempt(sa.Attempt);
            return RedirectToAction(nameof(Completed), new { sessionAttemptId });
        }
        return RedirectToAction(nameof(Break), new { attemptId = sa.AttemptId });
    }

    [HttpGet]
    [Route("/Student/Exam/Completed")]
    public async Task<IActionResult> Completed(int sessionAttemptId)
    {
        if (StudentId is null) return Challenge();
        var session = await _context.ExamSessionAttempts.AsNoTracking()
            .Include(s => s.Attempt).ThenInclude(a => a!.Exam).Include(s => s.Session)
            .FirstOrDefaultAsync(s => s.SessionAttemptId == sessionAttemptId && s.Attempt!.StudentId == StudentId);
        if (session is null) return NotFound();
        if (session.Status == AttemptStatus.ProctoringBlocked || session.Attempt?.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId = session.AttemptId });
        if (session.Status is not (AttemptStatus.Completed or AttemptStatus.AutoSubmitted))
            return RedirectToAction(nameof(Session), new { sessionAttemptId });
        return View(session);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/SaveAnswer")]
    public async Task<IActionResult> SaveAnswer(int sessionAttemptId, int questionId, string? selectedOption)
    {
        if (StudentId is null) return Unauthorized();
        var sa = await _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Session)
            .FirstOrDefaultAsync(x => x.SessionAttemptId == sessionAttemptId && x.Attempt!.StudentId == StudentId);
        if (sa is null || sa.Session is null || sa.FrozenAtUtc.HasValue || sa.Status != AttemptStatus.InProgress ||
            sa.Attempt?.Status == AttemptStatus.ProctoringBlocked)
        {
            if (sa?.Attempt?.Status == AttemptStatus.ProctoringBlocked)
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    ok = false,
                    blocked = true,
                    redirectUrl = Url.Action(nameof(Blocked), new { attemptId = sa.AttemptId })
                });
            return BadRequest(new { ok = false, message = "Attempt is not active." });
        }
        var expiry = (sa.StartedAt ?? DateTime.Now).AddMinutes(sa.Session.DurationMinutes);
        if (DateTime.Now >= expiry)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, expired = true, message = "Session time has ended." });
        var currentQuestion = await GetSessionQuestionAsync(sa, questionId);
        if (!await HasRecentIdentityAsync(sessionAttemptId))
            return StatusCode(428, new
            {
                ok = false,
                identityRequired = true,
                redirectUrl = Url.Action(nameof(Session), new { sessionAttemptId })
            });
        if (currentQuestion is null) return BadRequest(new { ok = false, message = "Invalid question." });
        var option = string.IsNullOrWhiteSpace(selectedOption) ? null : selectedOption.Trim().ToUpperInvariant();
        if (option is not null && !IsValidOption(currentQuestion, option))
            return BadRequest(new { ok = false, message = "Invalid option for this question." });
        var answer = await _context.StudentAnswers.FirstOrDefaultAsync(a => a.SessionAttemptId == sessionAttemptId && a.QuestionId == questionId);
        if (answer is null) { answer = new StudentAnswer { SessionAttemptId = sessionAttemptId, QuestionId = questionId }; _context.StudentAnswers.Add(answer); }
        answer.SelectedOption = option; answer.AnsweredAt = DateTime.Now; answer.IsCorrect = null; answer.MarksAwarded = 0;
        sa.LastQuestionId = questionId;
        sa.CurrentQuestionIndex = (await GetSessionQuestionsAsync(sa)).FindIndex(q => q.QuestionId == questionId);
        await _context.SaveChangesAsync();
        return Json(new { ok = true, savedAt = DateTime.Now.ToString("HH:mm:ss") });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/ToggleReview")]
    public async Task<IActionResult> ToggleReview(int sessionAttemptId, int questionId, bool marked)
    {
        if (StudentId is null) return Unauthorized();
        var sa = await _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Session)
            .FirstOrDefaultAsync(x => x.SessionAttemptId == sessionAttemptId && x.Attempt!.StudentId == StudentId);
        if (sa is null || sa.Session is null || sa.FrozenAtUtc.HasValue || sa.Status != AttemptStatus.InProgress ||
            sa.Attempt?.Status == AttemptStatus.ProctoringBlocked)
        {
            if (sa?.Attempt?.Status == AttemptStatus.ProctoringBlocked)
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    ok = false,
                    blocked = true,
                    redirectUrl = Url.Action(nameof(Blocked), new { attemptId = sa.AttemptId })
                });
            return BadRequest(new { ok = false });
        }
        var expiry = (sa.StartedAt ?? DateTime.Now).AddMinutes(sa.Session.DurationMinutes);
        if (DateTime.Now >= expiry)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, expired = true });
        if (await GetSessionQuestionAsync(sa, questionId) is null) return BadRequest(new { ok = false });
        var answer = await _context.StudentAnswers.FirstOrDefaultAsync(a => a.SessionAttemptId == sessionAttemptId && a.QuestionId == questionId);
        if (answer is null) { answer = new StudentAnswer { SessionAttemptId = sessionAttemptId, QuestionId = questionId }; _context.StudentAnswers.Add(answer); }
        answer.IsMarkedForReview = marked; answer.AnsweredAt = DateTime.Now;
        await _context.SaveChangesAsync();
        return Json(new { ok = true, marked });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/SavePosition")]
    public async Task<IActionResult> SavePosition(int sessionAttemptId, int questionId)
    {
        if (StudentId is null) return Unauthorized();
        var sessionAttempt = await _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Session)
            .FirstOrDefaultAsync(x =>
                x.SessionAttemptId == sessionAttemptId &&
                x.Attempt!.StudentId == StudentId);

        if (sessionAttempt?.Attempt?.Exam is null || sessionAttempt.Session is null ||
            sessionAttempt.Status != AttemptStatus.InProgress ||
            sessionAttempt.Attempt.Status != AttemptStatus.InProgress)
        {
            return BadRequest(new { ok = false });
        }

        var belongsToSession = await _context.ExamQuestions.AsNoTracking().AnyAsync(x =>
            x.ExamId == sessionAttempt.Attempt.ExamId && x.SessionId == sessionAttempt.SessionId &&
            x.QuestionId == questionId && x.IsActive && x.Question!.IsActive, HttpContext.RequestAborted);
        if (!belongsToSession) return BadRequest(new { ok = false });

        if (sessionAttempt.LastQuestionId != questionId)
        {
            sessionAttempt.LastQuestionId = questionId;
            sessionAttempt.CurrentQuestionIndex = (await GetSessionQuestionsAsync(sessionAttempt)).FindIndex(q => q.QuestionId == questionId);
            await _context.SaveChangesAsync();
        }

        return Json(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/LogProctoring")]
    public async Task<IActionResult> LogProctoring(
        [FromForm] ProctoringEventRequest request,
        CancellationToken cancellationToken)
    {
        if (StudentId is null)
            return Unauthorized();

        var allowedTypes = new Dictionary<ProctoringAlertType, ProctoringSeverity>
        {
            [ProctoringAlertType.TabSwitch] = ProctoringSeverity.Medium,
            [ProctoringAlertType.FullScreenExit] = ProctoringSeverity.Medium,
            [ProctoringAlertType.CameraAccessLost] = ProctoringSeverity.High,
            [ProctoringAlertType.MicrophoneAccessLost] = ProctoringSeverity.Medium,
            [ProctoringAlertType.MultipleDisplaysDetected] = ProctoringSeverity.Medium,
            [ProctoringAlertType.ScreenshotAttempt] = ProctoringSeverity.Medium,
            [ProctoringAlertType.AudioDetected] = ProctoringSeverity.Low,
            [ProctoringAlertType.TalkingDetected] = ProctoringSeverity.Medium,
            [ProctoringAlertType.AnswerPromptSpeech] = ProctoringSeverity.High,
            [ProctoringAlertType.Other] = ProctoringSeverity.Low
        };
        if (!Enum.TryParse<ProctoringAlertType>(request.AlertType, true, out var alertType) ||
            !allowedTypes.TryGetValue(alertType, out var severity))
        {
            return BadRequest(new { ok = false, message = "Unsupported proctoring event type." });
        }

        var active = await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId);
        if (active?.Attempt is null)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });

        var clientEventId = string.IsNullOrWhiteSpace(request.ClientEventId)
            ? Guid.NewGuid().ToString("N")
            : request.ClientEventId.Trim();
        if (clientEventId.Length > 64)
            return BadRequest(new { ok = false, message = "Invalid event identifier." });

        var duplicate = await _context.ProctoringLogs.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.StudentId == StudentId &&
                x.ClientEventId == clientEventId,
                cancellationToken);
        if (duplicate is not null)
        {
            return Json(new
            {
                ok = true,
                duplicate = true,
                observationOnly = true,
                logId = duplicate.LogId,
                blocked = false
            });
        }

        string? evidenceKey = null;
        if (_proctoringOptions.CaptureEvidence && !string.IsNullOrWhiteSpace(request.EvidenceDataUrl))
        {
            evidenceKey = await _evidenceStore.SaveDataUrlAsync(
                StudentId,
                active.Attempt.ExamId,
                active.AttemptId,
                request.EvidenceDataUrl,
                cancellationToken);
        }

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? alertType.ToString()
            : request.Description.Trim();
        if (description.Length > 500) description = description[..500];

        var log = new ProctoringLog
        {
            StudentId = StudentId,
            ExamId = active.Attempt.ExamId,
            AttemptId = active.AttemptId,
            SessionAttemptId = active.SessionAttemptId,
            AlertType = alertType,
            Severity = severity,
            Description = description,
            ScreenshotPath = evidenceKey,
            ConfidenceScore = request.ConfidenceScore,
            DetectedFaceCount = request.DetectedFaceCount,
            DurationSeconds = request.DurationSeconds,
            ModelName = "Browser Session Controls",
            ModelVersion = "BrowserControls-v1",
            ClientEventId = clientEventId,
            FrameCapturedAt = request.FrameCapturedAt ?? DateTime.Now,
            CountsAsViolation = false,
            PolicyPending = alertType is ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit,
            IsReviewed = false,
            CreatedDate = DateTime.Now
        };
        _context.ProctoringLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
        await _states.ApplyLogAsync(log.LogId, cancellationToken);

        return Json(new
        {
            ok = true,
            monitoringDisabled = false,
            observationOnly = true,
            module = "Browser Session Controls",
            status = "RecordedForReview",
            logId = log.LogId,
            countsAsViolation = false,
            ruleCount = 0,
            ruleLimit = 0,
            warningNumber = 0,
            warningLimit = 0,
            remainingWarnings = 0,
            blocked = false
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    [Route("/Student/Exam/VerifyActiveIdentity")]
    public async Task<IActionResult> VerifyActiveIdentity([FromForm] FaceIdentityFrameRequest request)
    {
        if (StudentId is null) return Unauthorized();
        var observationStartedAt = DateTime.Now;
        if (request.Frames.Count != _faceOptions.ContinuousVerificationFrames)
            return BadRequest(new { ok = false, message = $"Exactly {_faceOptions.ContinuousVerificationFrames} fresh frames are required." });

        var active = await _context.ExamSessionAttempts
            .Include(x => x.Attempt)
            .FirstOrDefaultAsync(x =>
                x.SessionAttemptId == request.SessionAttemptId &&
                x.AttemptId == request.AttemptId &&
                x.Attempt!.StudentId == StudentId);
        if (active?.Attempt?.Status == AttemptStatus.ProctoringBlocked || active?.Status == AttemptStatus.ProctoringBlocked)
            return StatusCode(423, new
            {
                ok = false,
                blocked = true,
                state = await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted)
            });
        if (active?.Status != AttemptStatus.InProgress || active.Attempt?.Status != AttemptStatus.InProgress)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });
        if (active?.Attempt is null) return BadRequest(new { ok = false, message = "Active examination session was not found." });

        var images = await ReadFaceFramesAsync(request.Frames, HttpContext.RequestAborted);
        if (images.Count != _faceOptions.ContinuousVerificationFrames)
            return BadRequest(new { ok = false, message = "One or more camera frames were invalid." });
        var result = await _faceIdentity.VerifyAsync(StudentId, images, HttpContext.RequestAborted);
        // Another model can lock the attempt while identity inference is in flight.
        var currentState = await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted);
        if (currentState?.Status == nameof(AttemptStatus.ProctoringBlocked))
            return StatusCode(423, new { ok = false, blocked = true, state = currentState });
        var presentationUnconfirmed = result.Data?.ReasonCode == "presentation_unconfirmed";
        if ((!result.Success && !presentationUnconfirmed) || result.Data is null)
        {
            await RecordIdentityUncertaintyAsync(active, result.Data?.ReasonCode ?? "service_unavailable", result.Message);
            return StatusCode(result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, message = IdentityMessage.For(result.Data?.ReasonCode, result.Message), status = result.Data?.Status, reasonCode = result.Data?.ReasonCode });
        }

        var criticalMismatch = result.Data.FrameCount > 0 && !presentationUnconfirmed &&
            !result.Data.Verified && result.Data.Status != "retry" && result.Data.ReasonCode == "identity_mismatch" &&
            1m - result.Data.MedianSimilarity > .45m;
        if (criticalMismatch)
        {
            var frameHash = string.Join(":", images.Select(image => Convert.ToHexString(SHA256.HashData(image.Bytes))[..16]).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            if (!await ConfirmIdentityMismatchAsync(active.AttemptId, active.SessionAttemptId,
                observationStartedAt, frameHash, result.Data.MedianSimilarity, HttpContext.RequestAborted))
                return Json(new
                {
                    ok = true,
                    verified = false,
                    confirmationPending = true,
                    retryAfterMilliseconds = 12000,
                    message = "Checking identity again. Please face the camera while fresh frames are compared."
                });
        }
        var isVerified = result.Data.Verified && !criticalMismatch;
        // Uncertain presentation and borderline similarity need a fresh sample;
        // neither is evidence that the candidate has been replaced.
        if (!isVerified && !criticalMismatch && (presentationUnconfirmed || result.Data.Status == "retry" || result.Data.ReasonCode != "identity_mismatch"))
        {
            await RecordIdentityUncertaintyAsync(active, result.Data.ReasonCode ?? "identity_uncertain", result.Data.Message ?? result.Message);
            return UnprocessableEntity(new
            {
                ok = false,
                verified = false,
                retry = true,
                reasonCode = result.Data.ReasonCode,
                message = IdentityMessage.For(result.Data.ReasonCode, result.Data.Message ?? result.Message)
            });
        }
        if (isVerified)
        {
            var recovered = await _context.ProctoringLogs.Where(x => x.SessionAttemptId == active.SessionAttemptId &&
                x.StudentId == StudentId && x.AlertType == ProctoringAlertType.ReviewRequested &&
                x.ModelName == "Identity monitoring health" && !x.IsReviewed).ToListAsync(HttpContext.RequestAborted);
            foreach (var health in recovered) health.IsReviewed = true;
        }
        string? identityEvidenceKey = null;
        if (!isVerified && _proctoringOptions.CaptureEvidence)
        {
            var evidenceFrame = images[^1];
            var prefix = evidenceFrame.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                ? "data:image/png;base64,"
                : "data:image/jpeg;base64,";
            try
            {
                identityEvidenceKey = await _evidenceStore.SaveDataUrlAsync(
                StudentId,
                active.Attempt.ExamId,
                active.AttemptId,
                prefix + Convert.ToBase64String(evidenceFrame.Bytes),
                    HttpContext.RequestAborted);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A full/unavailable evidence disk must not let a confirmed
                // substituted candidate continue the examination.
                HttpContext.RequestServices.GetService<ILogger<ExamController>>()?
                    .LogError(error, "Identity evidence failed for attempt {AttemptId}; lock will still be enforced", active.AttemptId);
            }
        }
        var log = new ProctoringLog
        {
            StudentId = StudentId,
            ExamId = active.Attempt.ExamId,
            AttemptId = active.AttemptId,
            SessionAttemptId = active.SessionAttemptId,
            AlertType = isVerified ? ProctoringAlertType.IdentityVerified : ProctoringAlertType.IdentityMismatch,
            Severity = isVerified ? ProctoringSeverity.Low : ProctoringSeverity.High,
            Description = presentationUnconfirmed ? "Camera presentation could not be authenticated. Fresh verification is required; this is not a misconduct finding." : $"Model 1 periodic identity observation: {result.Data.Status}; median={result.Data.MedianSimilarity:0.0000}, threshold={result.Data.Threshold:0.0000}, vote={result.Data.VoteRatio:0.00}.",
            ScreenshotPath = identityEvidenceKey,
            ConfidenceScore = result.Data.MedianSimilarity,
            DetectedFaceCount = 1,
            ModelName = _faceOptions.ModelName,
            ModelVersion = result.Data.ModuleVersion ?? _faceOptions.ModelVersion,
            ClientEventId = Guid.NewGuid().ToString("N"),
            FrameCapturedAt = observationStartedAt,
            CountsAsViolation = false,
            PolicyPending = criticalMismatch,
            IsReviewed = isVerified,
            CreatedDate = DateTime.Now
        };
        _context.ProctoringLogs.Add(log);
        await _context.SaveChangesAsync();
        if (criticalMismatch) await _states.ApplyLogAsync(log.LogId, HttpContext.RequestAborted);
        if (criticalMismatch)
        {
            var confirmedState = await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted);
            if (confirmedState?.Status == nameof(AttemptStatus.ProctoringBlocked))
                return StatusCode(423, new { ok = false, blocked = true, state = confirmedState });
            return Json(new
            {
                ok = true,
                verified = false,
                identityMismatchConfirmed = true,
                message = "Identity mismatch confirmed. Your teacher has been notified for urgent review.",
                blocked = false,
                state = confirmedState
            });
        }
        var verifiedAt = DateTime.UtcNow;
        var entryTicket = isVerified ? _verificationTickets.Create(new ProctoringVerificationTicket(
            StudentId, active.Attempt.ExamId, result.Data.MedianSimilarity,
            result.Data.ModuleVersion ?? _faceOptions.ModelVersion, verifiedAt,
            verifiedAt.AddSeconds(20), $"session:{active.SessionAttemptId}:{log.LogId}")) : null;
        return Json(new
        {
            ok = true,
            verified = isVerified,
            entryTicket,
            candidateName = await _context.Users.Where(x => x.Id == StudentId)
                .Select(x => x.FullName).FirstOrDefaultAsync() ?? "the registered candidate",
            identityRequired = !isVerified,
            redirectUrl = Url.Action(nameof(Session), new { sessionAttemptId = active.SessionAttemptId }),
            status = result.Data.Status,
            score = result.Data.MedianSimilarity,
            threshold = result.Data.Threshold,
            observationOnly = true,
            blocked = criticalMismatch,
            logId = log.LogId
        });
    }

    private async Task RecordIdentityUncertaintyAsync(ExamSessionAttempt active, string reason, string message)
    {
        // A technical/quality problem must remain visible to the owning teacher,
        // but must never be processed as a strike or an identity mismatch.
        var since = DateTime.Now.AddMinutes(-1);
        if (await _context.ProctoringLogs.AnyAsync(x => x.SessionAttemptId == active.SessionAttemptId &&
            x.ModelName == "Identity monitoring health" && x.CreatedDate >= since, HttpContext.RequestAborted)) return;
        var description = $"Identity verification needs review ({reason}). {message} No substitution has been confirmed; background retries continue.";
        _context.ProctoringLogs.Add(new ProctoringLog
        {
            StudentId = StudentId!,
            ExamId = active.Attempt!.ExamId,
            AttemptId = active.AttemptId,
            SessionAttemptId = active.SessionAttemptId,
            AlertType = ProctoringAlertType.ReviewRequested,
            Severity = ProctoringSeverity.Low,
            ModelName = "Identity monitoring health",
            ModelVersion = _faceOptions.ModelVersion,
            Description = description.Length > 500 ? description[..500] : description,
            CountsAsViolation = false,
            PolicyPending = false,
            PolicyProcessed = true,
            CreatedDate = DateTime.Now
        });
        await _context.SaveChangesAsync(HttpContext.RequestAborted);
    }

    private async Task<bool> HasRecentIdentityAsync(int sessionAttemptId, string? entryNonce = null)
    {
        // Manual enforcement never lets an AI observation revoke an already admitted
        // candidate's answer access. Initial entry still requires a fresh protected ticket.
        if (entryNonce is null && await _context.ExamSessionAttempts.AsNoTracking().AnyAsync(s =>
            s.SessionAttemptId == sessionAttemptId && s.Attempt!.StudentId == StudentId &&
            s.Status == AttemptStatus.InProgress && s.Attempt.Status == AttemptStatus.InProgress &&
            s.Attempt.Exam!.BlockingMode == ExamBlockingMode.MANUAL_ONLY) &&
            await _context.ProctoringLogs.AsNoTracking().AnyAsync(l => l.StudentId == StudentId &&
                l.SessionAttemptId == sessionAttemptId && l.AlertType == ProctoringAlertType.IdentityVerified)) return true;
        var latest = await _context.ProctoringLogs.AsNoTracking()
            .Where(x => x.StudentId == StudentId && x.SessionAttemptId == sessionAttemptId &&
                (x.AlertType == ProctoringAlertType.IdentityVerified || x.AlertType == ProctoringAlertType.IdentityMismatch))
            .OrderByDescending(x => x.LogId)
            .Where(x => x.Description == null || !x.Description.StartsWith("Identity recheck pending: "))
            .Select(x => new { x.LogId, x.AlertType, x.CreatedDate }).FirstOrDefaultAsync();
        var fresh = latest is not null && latest.CreatedDate >= DateTime.Now.AddSeconds(-90);
        // A recently observed image-quality problem does not revoke a previously
        // verified running session. It never grants initial entry or conceals outages.
        if (!fresh && entryNonce is null && latest?.AlertType == ProctoringAlertType.IdentityVerified)
        {
            var since = DateTime.Now.AddSeconds(-90);
            fresh = await _context.ExamSessionAttempts.AsNoTracking().AnyAsync(s => s.SessionAttemptId == sessionAttemptId &&
                s.Attempt!.StudentId == StudentId && s.Status == AttemptStatus.InProgress && s.Attempt.Status == AttemptStatus.InProgress) &&
                await _context.ProctoringLogs.AsNoTracking().AnyAsync(l => l.StudentId == StudentId && l.SessionAttemptId == sessionAttemptId &&
                    l.ModelName == "Identity monitoring health" && l.CreatedDate >= since && l.Description != null &&
                    (l.Description.StartsWith("Identity verification needs review (poor_lighting).") ||
                     l.Description.StartsWith("Identity verification needs review (blurry_face).") ||
                     l.Description.StartsWith("Identity verification needs review (low_face_quality).") ||
                     l.Description.StartsWith("Identity verification needs review (face_too_small).")));
        }
        return latest is not null && latest.AlertType == ProctoringAlertType.IdentityVerified && fresh &&
            (entryNonce is null || entryNonce == $"session:{sessionAttemptId}:{latest.LogId}");
    }

    private async Task RestoreReleasedWaitingTimeAsync(int sessionAttemptId)
    {
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var session = await _context.ExamSessionAttempts.Include(x => x.Attempt).Include(x => x.Session)
                .FirstOrDefaultAsync(x => x.SessionAttemptId == sessionAttemptId && x.Attempt!.StudentId == StudentId &&
                    x.Status == AttemptStatus.InProgress && x.Attempt.Status == AttemptStatus.InProgress);
            if (session?.Attempt is not null)
            {
                var released = await _context.ProctoringLogs.Where(x => x.SessionAttemptId == sessionAttemptId &&
                    x.AlertType == ProctoringAlertType.ExamUnblocked).OrderByDescending(x => x.LogId).FirstOrDefaultAsync();
                if (released is not null && !await _context.ProctoringLogs.AnyAsync(x => x.SessionAttemptId == sessionAttemptId &&
                    x.AlertType == ProctoringAlertType.ExamResumed && x.LogId > released.LogId))
                {
                    var now = DateTime.Now;
                    var waiting = now > released.CreatedDate ? now - released.CreatedDate : TimeSpan.Zero;
                    if (session.FrozenAtUtc.HasValue)
                        session.StartedAt = now.AddSeconds(session.RemainingTimeSeconds ?? 0).AddMinutes(-session.Session!.DurationMinutes);
                    else if (session.StartedAt.HasValue) session.StartedAt = session.StartedAt.Value.Add(waiting);
                    session.FrozenAtUtc = null;
                    _context.ProctoringLogs.Add(new ProctoringLog
                    {
                        StudentId = StudentId!,
                        ExamId = session.Attempt.ExamId,
                        AttemptId = session.AttemptId,
                        SessionAttemptId = sessionAttemptId,
                        AlertType = ProctoringAlertType.ExamResumed,
                        Description = "Candidate reopened the released session. Waiting time since teacher release was restored.",
                        CountsAsViolation = false,
                        IsReviewed = true,
                        CreatedDate = now,
                        ModelName = "Session navigation",
                        ClientEventId = Guid.NewGuid().ToString("N")
                    });
                    await _context.SaveChangesAsync();
                }
            }
            await transaction.CommitAsync();
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(30_000_000)]
    [Route("/Student/Exam/StartBehaviorSession")]
    public async Task<IActionResult> StartBehaviorSession([FromForm] BehaviorFrameRequest request)
    {
        if (StudentId is null) return Unauthorized();
        var expectedFrames = _behaviorOptions.BaselineFrames + (request.ScreenCalibration ? 8 : 0);
        if (expectedFrames > 20 || request.Frames.Count != expectedFrames)
            return BadRequest(new { ok = false, message = $"Exactly {expectedFrames} clear baseline frames are required." });

        var active = await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId);
        if (active?.Attempt is null)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });

        var images = await ReadFaceFramesAsync(request.Frames, HttpContext.RequestAborted);
        if (images.Count != expectedFrames)
            return BadRequest(new { ok = false, message = "One or more baseline frames were invalid." });

        var result = await _behaviorAnalysis.StartSessionAsync(
            BuildBehaviorSessionKey(StudentId, request.AttemptId, request.SessionAttemptId),
            images,
            HttpContext.RequestAborted, request.ScreenCalibration);
        if (!result.Success || result.Data is null)
        {
            var statusCode = result.ServiceUnavailable
                ? StatusCodes.Status503ServiceUnavailable
                : result.Message.Contains("not initialized", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(statusCode,
                new { ok = false, message = result.Message });
        }

        return Json(new
        {
            ok = true,
            started = result.Data.Started,
            observationOnly = true,
            modelVersion = result.Data.ModuleVersion,
            calibrationStatus = result.Data.CalibrationStatus
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(8_000_000)]
    [Route("/Student/Exam/AnalyzeBehavior")]
    public async Task<IActionResult> AnalyzeBehavior([FromForm] BehaviorSingleFrameRequest request)
    {
        if (StudentId is null) return Unauthorized();
        var observationStartedAt = DateTime.Now;
        if (request.Frame is null)
            return BadRequest(new { ok = false, message = "A fresh camera frame is required." });

        var active = await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId);
        if (active?.Attempt is null)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });

        var images = await ReadFaceFramesAsync(new[] { request.Frame }, HttpContext.RequestAborted);
        if (images.Count != 1)
            return BadRequest(new { ok = false, message = "The camera frame was invalid." });

        var image = images[0];
        var result = await _behaviorAnalysis.AnalyzeAsync(
            BuildBehaviorSessionKey(StudentId, request.AttemptId, request.SessionAttemptId),
            image,
            HttpContext.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            var statusCode = result.ServiceUnavailable
                ? StatusCodes.Status503ServiceUnavailable
                : result.Message.Contains("not initialized", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(statusCode,
                new { ok = false, message = result.Message });
        }

        if (result.Data.Signals is null)
            return StatusCode(503, new { ok = false, message = "Restart Model 2: incident signal contract is unavailable." });
        var metrics = result.Data.Metrics;
        var observations = new List<IncidentObservation>();
        foreach (var key in new[] { "attention", "persons", "absence", "mouth" })
        {
            var type = key == "persons" ? ProctoringAlertType.MultipleFaces : key == "absence"
                ? ProctoringAlertType.FaceNotDetected : ProctoringAlertType.LookAway;
            observations.Add(new(key, result.Data.Signals.GetValueOrDefault(key), new ProctoringLog
            {
                AlertType = type,
                Severity = key == "attention" ? ProctoringSeverity.Medium : ProctoringSeverity.High,
                Description = $"[{result.Data.SignalCodes.GetValueOrDefault(key, "LOOK_AWAY")}] Model 2 calibrated {key}: faces={metrics?.FaceCount}; yaw={metrics?.Yaw:0.0}; pitch={metrics?.Pitch:0.0}; roll={metrics?.Roll:0.0}. Teacher review required.",
                DetectedFaceCount = metrics?.FaceCount,
                ModelName = _behaviorOptions.ModelName,
                ModelVersion = result.Data.ModuleVersion ?? _behaviorOptions.ModelVersion
            }));
        }
        var logIds = await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId,
            observationStartedAt, observations, HttpContext.RequestAborted);
        await AttachObservationImageAsync(logIds, image, active.Attempt.ExamId, active.AttemptId);
        return Json(new
        {
            ok = true,
            observationOnly = true,
            metrics,
            events = result.Data.Events,
            logIds,
            state = logIds.Count > 0 ? await _states.GetAsync(active.AttemptId, HttpContext.RequestAborted) : null
        });
    }

    private async Task AttachObservationImageAsync(IReadOnlyList<int> logIds, FaceIdentityImage image, int examId, int attemptId)
    {
        if (logIds.Count == 0 || !_proctoringOptions.CaptureEvidence || StudentId is null) return;
        var prefix = image.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            ? "data:image/png;base64," : "data:image/jpeg;base64,";
        var key = await _evidenceStore.SaveDataUrlAsync(StudentId, examId, attemptId,
            prefix + Convert.ToBase64String(image.Bytes), HttpContext.RequestAborted);
        var logs = await _context.ProctoringLogs.Where(x => logIds.Contains(x.LogId) && x.StudentId == StudentId).ToListAsync();
        foreach (var log in logs) log.ScreenshotPath ??= key;
        await _context.SaveChangesAsync(HttpContext.RequestAborted);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(8_000_000)]
    [Route("/Student/Exam/AnalyzeObjects")]
    public async Task<IActionResult> AnalyzeObjects([FromForm] ObjectDetectionFrameRequest request)
    {
        if (StudentId is null) return Unauthorized();
        var observationStartedAt = DateTime.Now;
        if (request.Frame is null)
            return BadRequest(new { ok = false, message = "A fresh camera frame is required." });

        var active = await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId);
        if (active?.Attempt is null)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });

        var images = await ReadFaceFramesAsync(new[] { request.Frame }, HttpContext.RequestAborted);
        if (images.Count != 1)
            return BadRequest(new { ok = false, message = "The camera frame was invalid." });

        var image = images[0];
        var result = await _objectDetection.AnalyzeAsync(image, HttpContext.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            return StatusCode(
                result.ServiceUnavailable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, ready = false, message = result.Message });
        }

        var detections = result.Data.Detections
            .Where(x => x.AlertEligible && x.Confidence >= _objectOptions.ConfidenceThreshold)
            .Where(x => ProctoringObjectPolicy.BannedLabels.Contains(x.Label))
            .OrderByDescending(x => x.Confidence)
            .ToList();
        // Face presence is authoritative in Model 2. Do not count YOLO extra_person again as an object.
        detections = detections.Where(x => x.Label != "extra_person").ToList();
        var labels = string.Join(", ", detections.Select(x => x.Label).Distinct().OrderBy(x => x));
        var log = new ProctoringLog
        {
            AlertType = ProctoringAlertType.ElectronicDeviceDetected,
            Severity = ProctoringSeverity.Medium,
            Description = $"[OBJECT_{detections.FirstOrDefault()?.Label.ToUpperInvariant()}] Model 3 objects: {labels}; confidence={detections.FirstOrDefault()?.Confidence:0.0000}.",
            ConfidenceScore = detections.FirstOrDefault()?.Confidence,
            ModelName = _objectOptions.ModelName,
            ModelVersion = result.Data.ModuleVersion ?? _objectOptions.ModelVersion
        };
        var logIds = await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId,
            observationStartedAt, new[] { new IncidentObservation("objects", detections.Count > 0, log) }, HttpContext.RequestAborted);
        await AttachObservationImageAsync(logIds, image, active.Attempt.ExamId, active.AttemptId);
        return Json(new
        {
            ok = true,
            ready = true,
            clear = detections.Count == 0,
            duplicate = detections.Count > 0 && logIds.Count == 0,
            observationOnly = true,
            detections,
            observations = result.Data.Detections.Where(x => !x.AlertEligible),
            logIds,
            state = await _states.GetAsync(active.AttemptId, HttpContext.RequestAborted)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12_500_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12_500_000)]
    [Route("/Student/Exam/TranscribeSpeech")]
    public Task<IActionResult> TranscribeSpeech([FromForm] SpeechRecognitionRequest request) =>
        ProcessSpeechAsync(request, false);

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12_500_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12_500_000)]
    public Task<IActionResult> DetectSpeech([FromForm] SpeechRecognitionRequest request) =>
        ProcessSpeechAsync(request, true);

    private async Task<IActionResult> ProcessSpeechAsync(SpeechRecognitionRequest request, bool detectionOnly, SpeechRecognitionCallResult? streamedResult = null, DateTime? receivedAt = null)
    {
        if (StudentId is null) return Unauthorized();
        var observationStartedAt = receivedAt ?? DateTime.Now;
        if (request.Audio is null || request.Audio.Length <= 0)
            return BadRequest(new { ok = false, message = "A fresh microphone clip is required." });

        var maximumBytes = Math.Clamp(_speechOptions.MaximumAudioBytes, 250_000, 12_000_000);
        if (request.Audio.Length > maximumBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { ok = false, message = "The microphone clip is too large." });

        var mediaType = request.Audio.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio/webm", "audio/ogg", "audio/wav", "audio/x-wav", "audio/flac", "audio/x-flac", "audio/mpeg",
            "audio/mp4", "audio/x-m4a", "video/webm", "video/mp4"
        };
        if (!supported.Contains(mediaType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { ok = false, message = "Unsupported microphone clip format." });

        var active = await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId);
        if (active?.Attempt is null)
            return BadRequest(new { ok = false, message = "Active examination session was not found." });

        byte[] bytes;
        await using (var input = request.Audio.OpenReadStream())
        using (var buffer = new MemoryStream(capacity: checked((int)request.Audio.Length)))
        {
            await input.CopyToAsync(buffer, HttpContext.RequestAborted);
            bytes = buffer.ToArray();
        }
        if (bytes.Length == 0 || bytes.Length > maximumBytes)
            return BadRequest(new { ok = false, message = "The microphone clip was invalid." });

        var clipKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..32];
        var detectedLog = detectionOnly ? null : await _context.ProctoringLogs.Where(x =>
            x.StudentId == StudentId && x.AttemptId == active.AttemptId &&
            x.SessionAttemptId == active.SessionAttemptId && x.ClientEventId != null && x.ClientEventId.StartsWith(clipKey) &&
            (x.AlertType == ProctoringAlertType.TalkingDetected || x.AlertType == ProctoringAlertType.AnswerPromptSpeech || x.ModelName == "Speech screening"))
            .OrderByDescending(x => x.ModelName != "Speech screening").ThenByDescending(x => x.LogId).FirstOrDefaultAsync();
        if (!detectionOnly && request.DetectionLogId is int detectionId)
        {
            var recent = DateTime.Now.AddSeconds(-30);
            detectedLog = await _context.ProctoringLogs.FirstOrDefaultAsync(x => x.LogId == detectionId &&
                x.StudentId == StudentId && x.SessionAttemptId == active.SessionAttemptId && x.AttemptId == active.AttemptId &&
                (x.IncidentKey == "speech" || x.ModelName == "Speech screening") && x.CreatedDate >= recent);
        }
        var spokenQuestions = new List<Question>();
        // Delayed ASR must use the question captured by the trusted VAD screening,
        // never the question visible when transcription eventually finishes.
        var contextIds = !detectionOnly && detectedLog?.QuestionIdAtCapture is int capturedQuestion
            ? new[] { capturedQuestion }
            : request.QuestionIds.Append(request.QuestionId ?? 0).Where(id => id > 0).Distinct().ToArray();
        if (contextIds.Length == 0 && active.LastQuestionId.HasValue)
            contextIds = new[] { active.LastQuestionId.Value };
        if (contextIds.Length > 8) return BadRequest(new { ok = false, message = "Too many question contexts." });
        foreach (var questionId in contextIds)
        {
            var ownedQuestion = await GetSessionQuestionAsync(active, questionId);
            if (ownedQuestion is null) return BadRequest(new { ok = false, message = "Invalid question context." });
            spokenQuestions.Add(ownedQuestion);
        }
        var audio = new SpeechAudio(bytes, mediaType, request.Audio.FileName)
        {
            DetectionOnly = detectionOnly,
            Contexts = spokenQuestions.Select(q => new SpeechQuestionContext(q.QuestionId, q.QuestionText,
                new[] { q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.OptionE ?? "" })).ToArray()
        };
        var result = streamedResult ?? await _speechRecognition.TranscribeAsync(audio, HttpContext.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            return StatusCode(
                result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, ready = false, message = result.Message });
        }

        var transcript = string.Join(' ', result.Data.Transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var vadSpeech = result.Data.VadSpeechSeconds >= .3;
        if (!result.Data.SpeechDetected || (!vadSpeech && (string.IsNullOrWhiteSpace(transcript) ||
            result.Data.Confidence < _speechOptions.MinimumTranscriptConfidence)))
        {
            if (detectionOnly)
                await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId, observationStartedAt,
                    new[] { new IncidentObservation("speech", false, new ProctoringLog()) }, HttpContext.RequestAborted);
            return Json(new
            {
                ok = true,
                ready = true,
                speechDetected = false,
                observationOnly = true,
                language = result.Data.Language,
                confidence = result.Data.Confidence,
                logIds = Array.Empty<int>()
            });
        }

        if (detectionOnly)
        {
            // VAD detects speech presence, not speaker identity. Require time-aligned mouth support.
            var incidentState = string.IsNullOrWhiteSpace(active.ProctoringIncidentsJson)
                ? new Dictionary<string, IncidentEpisode>()
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, IncidentEpisode>>(active.ProctoringIncidentsJson) ?? new();
            var mouthSupported = SpeechAttributionPolicy.MouthSupports(incidentState.GetValueOrDefault("mouth"), observationStartedAt.ToUniversalTime());
            var previousScreening = await _context.ProctoringLogs.AsNoTracking()
                .Where(x => x.SessionAttemptId == active.SessionAttemptId && x.ModelName == "Speech screening")
                .OrderByDescending(x => x.LogId).FirstOrDefaultAsync();
            var conversation = previousScreening is not null && observationStartedAt - previousScreening.CreatedDate < TimeSpan.FromSeconds(10)
                ? previousScreening.ModelVersion : Guid.NewGuid().ToString("N");
            var screening = new ProctoringLog
            {
                StudentId = StudentId,
                ExamId = active.Attempt.ExamId,
                AttemptId = active.AttemptId,
                SessionAttemptId = active.SessionAttemptId,
                AlertType = ProctoringAlertType.Other,
                ModelName = "Speech screening",
                ModelVersion = conversation,
                Description = "Speech present; speaker attribution is uncertain until correlated with video. Awaiting question context.",
                QuestionIdAtCapture = request.QuestionId ?? active.LastQuestionId,
                SpeakerAttribution = mouthSupported ? "mouth_correlated" : "unknown",
                DetectedFaceCount = null,
                CountsAsViolation = false,
                PolicyProcessed = true,
                FrameCapturedAt = observationStartedAt,
                ClientEventId = clipKey + Guid.NewGuid().ToString("N"),
                CreatedDate = DateTime.Now
            };
            _context.ProctoringLogs.Add(screening);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            IReadOnlyList<int> immediateIds = Array.Empty<int>();
            if (vadSpeech && mouthSupported)
            {
                // Temporal mouth correlation supports attribution; VAD alone never does.
                var immediate = new ProctoringLog
                {
                    AlertType = ProctoringAlertType.TalkingDetected,
                    Severity = ProctoringSeverity.Medium,
                    QuestionIdAtCapture = request.QuestionId ?? active.LastQuestionId,
                    SpeakerAttribution = "mouth_correlated",
                    Description = $"[CANDIDATE_SPEECH] conversation={conversation}; Speech coincides with candidate mouth movement; transcript analysis is pending.",
                    ModelName = _speechOptions.ModelName,
                    ModelVersion = result.Data.ModuleVersion,
                    ClientEventId = clipKey + Guid.NewGuid().ToString("N"),
                    DurationSeconds = (int)Math.Ceiling(result.Data.VadSpeechSeconds)
                };
                immediateIds = await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId, observationStartedAt,
                    new[] { new IncidentObservation("speech", true, immediate) }, HttpContext.RequestAborted);
            }
            if (_proctoringOptions.CaptureEvidence && immediateIds.Count > 0)
            {
                var evidenceKey = await _evidenceStore.SaveUploadAsync(StudentId, active.Attempt.ExamId, active.AttemptId, request.Audio, HttpContext.RequestAborted);
                var speechEvidence = await _context.ProctoringLogs.SingleAsync(x => x.LogId == immediateIds[0]);
                speechEvidence.RecordingPath = evidenceKey; speechEvidence.RecordingIncludesAudio = evidenceKey is not null;
                await _context.SaveChangesAsync(HttpContext.RequestAborted);
            }
            var latestSpeech = await _context.ProctoringLogs.AsNoTracking().Where(x => x.AttemptId == active.AttemptId && x.SessionAttemptId == active.SessionAttemptId && x.IncidentKey == "speech")
                .OrderByDescending(x => x.LogId).FirstOrDefaultAsync();
            return Json(new
            {
                ok = true,
                speechDetected = true,
                needsContext = true,
                logIds = immediateIds,
                detectionLogId = immediateIds.Count > 0 ? latestSpeech?.LogId : screening.LogId,
                softReminderId = immediateIds.Count > 0 && latestSpeech?.ModelName == "Speech soft reminder" ? latestSpeech.LogId : (int?)null,
                state = await _states.GetAsync(active.AttemptId, HttpContext.RequestAborted)
            });
        }

        var semantic = result.Data.ContextAnalysis;
        var spokenQuestion = spokenQuestions.FirstOrDefault(q => q.QuestionId == semantic?.QuestionId);
        var contextMatch = new SpeechContextMatch(SpeechContextAnalyzer.IsHighSeverity(semantic) && semantic?.QuestionId is int semanticQuestionId && spokenQuestions.Any(q => q.QuestionId == semanticQuestionId), semantic?.Reason ?? "Semantic comparison unavailable; retain observation for review.");
        var highSeveritySpeech = SpeechContextAnalyzer.IsHighSeverity(semantic) && semantic?.QuestionId is int matchedId && spokenQuestions.Any(q => q.QuestionId == matchedId);
        if (highSeveritySpeech)
            contextMatch = new SpeechContextMatch(true, "Semantic similarity exceeded 0.60; teacher review required.");
        var speechAlert = (contextMatch.Related || highSeveritySpeech) ? ProctoringAlertType.AnswerPromptSpeech : ProctoringAlertType.TalkingDetected;
        // The same audio already received its warning in the fast VAD lane.
        // Enrich its teacher evidence without issuing another strike.
        if (detectedLog is not null && detectedLog.ModelName != "Speech screening")
        {
            var detail = $"[{(contextMatch.Related ? "QUESTION_RELATED_SPEECH" : "SPEECH_DETECTED")}] Speech context (review required): similarity={semantic?.Score:0.000}; highSeverity={highSeveritySpeech}; related={contextMatch.Related}; {contextMatch.Reason}; {transcript}";
            if (contextMatch.Related)
            {
                if (highSeveritySpeech) detectedLog.Severity = ProctoringSeverity.High;
                detectedLog.AlertType = ProctoringAlertType.AnswerPromptSpeech;
            }
            detectedLog.Description = detail.Length <= 500 ? detail : detail[..500];
            detectedLog.SpeechTranscript = transcript[..Math.Min(transcript.Length, 4000)];
            detectedLog.SemanticSimilarity = semantic?.Score is double matchedScore && double.IsFinite(matchedScore) && matchedScore is >= -1 and <= 1 ? (decimal)matchedScore : null;
            detectedLog.MatchedQuestionId = spokenQuestion?.QuestionId;
            detectedLog.SpeakerAttribution ??= "mouth_correlated";
            // Keep the clip that actually produced this transcript as private evidence.
            if (_proctoringOptions.CaptureEvidence && (detectedLog.RecordingPath is null || Path.GetExtension(detectedLog.RecordingPath).Equals(".wav", StringComparison.OrdinalIgnoreCase)))
            {
                detectedLog.RecordingPath = await _evidenceStore.SaveUploadAsync(StudentId, active.Attempt.ExamId, active.AttemptId, request.Audio, HttpContext.RequestAborted);
                detectedLog.RecordingIncludesAudio = detectedLog.RecordingPath is not null;
            }
            if (_proctoringOptions.CaptureEvidence && !string.IsNullOrWhiteSpace(request.QuestionScreenshotDataUrl))
                detectedLog.ScreenshotPath = await _evidenceStore.SaveDataUrlAsync(StudentId, active.Attempt.ExamId, active.AttemptId, request.QuestionScreenshotDataUrl, HttpContext.RequestAborted) ?? detectedLog.ScreenshotPath;
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            if (highSeveritySpeech) await _states.ApplyLogAsync(detectedLog.LogId, HttpContext.RequestAborted);
            if (contextMatch.Related) await _states.NotifySpeechContextAsync(detectedLog.LogId, HttpContext.RequestAborted);
            return Json(new
            {
                ok = true,
                speechDetected = true,
                duplicate = true,
                questionRelated = contextMatch.Related,
                semanticReady = semantic?.Ready == true,
                transcript,
                language = result.Data.Language,
                logIds = Array.Empty<int>()
            });
        }
        // Only server-screened speech may enter the semantic evidence path.
        if (detectedLog is null)
            return Json(new { ok = true, speechDetected = true, backgroundSpeech = true, duplicate = true, logIds = Array.Empty<int>() });
        if (!highSeveritySpeech) return Json(new { ok = true, speechDetected = true, questionRelated = false, semanticReady = semantic?.Ready == true, contextReason = semantic?.Reason, transcript, logIds = Array.Empty<int>() });
        if (await _context.ProctoringLogs.AnyAsync(x => x.SessionAttemptId == active.SessionAttemptId &&
            x.IncidentKey == "speech" && x.Description != null && x.Description.Contains("conversation=" + detectedLog.ModelVersion)))
            return Json(new { ok = true, speechDetected = true, duplicate = true, logIds = Array.Empty<int>() });
        observationStartedAt = DateTime.Now; // confirmation time; source clip timestamp remains in screening log


        var safeTranscript = transcript.Length > 250 ? transcript[..250] + "…" : transcript;
        // Keep the audio that produced the transcript, not a later clip captured
        // after inference has finished.
        var speechDescription = $"[{(contextMatch.Related ? "QUESTION_RELATED_SPEECH" : "SPEECH_DETECTED")}] conversation={detectedLog.ModelVersion}; Speech + Model 5, question {spokenQuestion?.QuestionId}; similarity={semantic?.Score:0.000}, ready={semantic?.Ready == true}, uncertain={semantic?.Uncertain == true}. {contextMatch.Reason} ({result.Data.Language}): {safeTranscript}";
        var log = new ProctoringLog
        {
            StudentId = StudentId,
            ExamId = active.Attempt.ExamId,
            AttemptId = active.AttemptId,
            SessionAttemptId = active.SessionAttemptId,
            AlertType = speechAlert,
            SpeechTranscript = transcript[..Math.Min(transcript.Length, 4000)],
            SemanticSimilarity = semantic?.Score is double speechScore && double.IsFinite(speechScore) && speechScore is >= -1 and <= 1 ? (decimal)speechScore : null,
            MatchedQuestionId = spokenQuestion?.QuestionId,
            QuestionIdAtCapture = detectedLog.QuestionIdAtCapture,
            SpeakerAttribution = "unknown",
            Severity = highSeveritySpeech ? ProctoringSeverity.High : ProctoringSeverity.Medium,
            Description = speechDescription.Length <= 500 ? speechDescription : speechDescription[..499] + "…",
            ConfidenceScore = result.Data.Confidence,
            DurationSeconds = (int)Math.Ceiling(result.Data.DurationSeconds),
            ModelName = _speechOptions.ModelName,
            ModelVersion = result.Data.ModuleVersion ?? _speechOptions.ModelVersion,
            ClientEventId = "speech-context:" + detectedLog.ModelVersion,
            FrameCapturedAt = observationStartedAt,
            CountsAsViolation = false,
            PolicyPending = true,
            IsReviewed = false,
            CreatedDate = DateTime.Now
        };
        var acceptedIds = await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId, observationStartedAt,
            new[] { new IncidentObservation("speech", true, log) }, HttpContext.RequestAborted);
        if (acceptedIds.Count == 0)
            return Json(new { ok = true, speechDetected = true, duplicate = true, logIds = Array.Empty<int>() });
        var speechRecording = _proctoringOptions.CaptureEvidence
            ? await _evidenceStore.SaveUploadAsync(StudentId, active.Attempt.ExamId,
                active.AttemptId, request.Audio, HttpContext.RequestAborted) : null;
        var persisted = await _context.ProctoringLogs.SingleAsync(x => x.LogId == acceptedIds[0]);
        if (_proctoringOptions.CaptureEvidence && !string.IsNullOrWhiteSpace(request.QuestionScreenshotDataUrl))
            persisted.ScreenshotPath = await _evidenceStore.SaveDataUrlAsync(StudentId, active.Attempt.ExamId, active.AttemptId, request.QuestionScreenshotDataUrl, HttpContext.RequestAborted);
        persisted.RecordingPath = speechRecording;
        persisted.RecordingIncludesAudio = speechRecording is not null;
        await _context.SaveChangesAsync(HttpContext.RequestAborted);

        return Json(new
        {
            ok = true,
            ready = true,
            speechDetected = true,
            questionRelated = contextMatch.Related,
            contextReason = contextMatch.Reason,
            semanticReady = semantic?.Ready == true,
            semanticScore = semantic?.Score,
            observationOnly = true,
            transcript,
            language = result.Data.Language,
            confidence = result.Data.Confidence,
            logIds = acceptedIds
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/EndBehaviorSession")]
    public async Task<IActionResult> EndBehaviorSession(int attemptId, int sessionAttemptId)
    {
        if (StudentId is null) return Unauthorized();
        var owned = await _context.ExamSessionAttempts.AsNoTracking()
            .AnyAsync(x => x.SessionAttemptId == sessionAttemptId &&
                           x.AttemptId == attemptId &&
                           x.Attempt!.StudentId == StudentId);
        if (!owned) return NotFound();

        var result = await _behaviorAnalysis.EndSessionAsync(
            BuildBehaviorSessionKey(StudentId, attemptId, sessionAttemptId),
            HttpContext.RequestAborted);
        return result.Success
            ? Json(new { ok = true })
            : StatusCode(result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, message = result.Message });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadRecordingSegment(int attemptId, int sessionAttemptId,
        string eventId, IFormFile? recording, bool includesAudio)
    {
        if (StudentId is null) return Unauthorized();
        if (!_proctoringOptions.CaptureEvidence || !Guid.TryParseExact(eventId, "N", out _) || recording is null)
            return BadRequest(new { ok = false });
        var active = await FindActiveSessionAsync(attemptId, sessionAttemptId);
        if (active?.Attempt is null) return BadRequest(new { ok = false });
        if (await _context.ProctoringLogs.AsNoTracking().AnyAsync(x => x.ClientEventId == eventId && x.StudentId == StudentId))
            return Json(new { ok = true, duplicate = true });
        var cutoff = DateTime.Now.AddMinutes(-1);
        var recent = await _context.ProctoringLogs.CountAsync(x => x.SessionAttemptId == sessionAttemptId &&
            x.AlertType == ProctoringAlertType.SessionRecording && x.CreatedDate >= cutoff);
        if (recent >= 12) return StatusCode(429, new { ok = false, message = "Recording uploads are arriving too quickly." });
        var path = await _evidenceStore.SaveUploadAsync(StudentId, active.Attempt.ExamId,
            attemptId, recording, HttpContext.RequestAborted);
        if (path is null) return BadRequest(new { ok = false, message = "Recording clip was invalid or too large." });
        _context.ProctoringLogs.Add(new ProctoringLog
        {
            StudentId = StudentId,
            ExamId = active.Attempt.ExamId,
            AttemptId = attemptId,
            SessionAttemptId = sessionAttemptId,
            AlertType = ProctoringAlertType.SessionRecording,
            Severity = ProctoringSeverity.Low,
            RecordingPath = path,
            RecordingIncludesAudio = includesAudio,
            Description = "Routine camera/microphone session segment. This is not a misconduct alert.",
            ModelName = "Session recorder",
            ModelVersion = "1",
            ClientEventId = eventId,
            CountsAsViolation = false,
            IsReviewed = true,
            CreatedDate = DateTime.Now
        });
        try { await _context.SaveChangesAsync(HttpContext.RequestAborted); }
        catch
        {
            await _evidenceStore.DeleteAsync(path, CancellationToken.None);
            throw;
        }
        return Json(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    [Route("/Student/Exam/AttachProctoringEvidence")]
    public async Task<IActionResult> AttachProctoringEvidence(
        [FromForm] ProctoringEvidenceUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (StudentId is null)
            return Unauthorized();

        if (!_proctoringOptions.CaptureEvidence ||
            request.LogId <= 0 ||
            request.EvidenceFile is null ||
            request.EvidenceFile.Length <= 0)
        {
            return BadRequest(new { ok = false, message = "Evidence upload is invalid." });
        }

        var allowedEvidenceTypes = new HashSet<ProctoringAlertType>
        {
            ProctoringAlertType.IdentityMismatch,
            ProctoringAlertType.FaceNotDetected,
            ProctoringAlertType.MultipleFaces,
            ProctoringAlertType.LookAway,
            ProctoringAlertType.AudioDetected,
            ProctoringAlertType.TalkingDetected,
            ProctoringAlertType.AnswerPromptSpeech,
            ProctoringAlertType.TabSwitch,
            ProctoringAlertType.FullScreenExit,
            ProctoringAlertType.CameraAccessLost,
            ProctoringAlertType.MicrophoneAccessLost,
            ProctoringAlertType.ScreenShareLost,
            ProctoringAlertType.MultipleDisplaysDetected,
            ProctoringAlertType.ScreenshotAttempt,
            ProctoringAlertType.ElectronicDeviceDetected,
            ProctoringAlertType.AiMonitoringUnavailable,
            ProctoringAlertType.HeadAway,
            ProctoringAlertType.GazeAway,
            ProctoringAlertType.EyesClosed,
            ProctoringAlertType.MouthMovement
        };

        var recentCutoff = DateTime.Now.AddMinutes(-5);
        var log = await _context.ProctoringLogs
            .Include(x => x.Student)
            .FirstOrDefaultAsync(x =>
                x.LogId == request.LogId &&
                x.StudentId == StudentId &&
                x.AttemptId != null &&
                x.SessionAttemptId != null &&
                x.CreatedDate >= recentCutoff,
                cancellationToken);

        if (log is null || !allowedEvidenceTypes.Contains(log.AlertType))
            return NotFound();

        // Client retries are idempotent: keep the first valid event clip rather
        // than replacing it with a later retry segment.
        if (!string.IsNullOrWhiteSpace(log.VideoEvidencePath))
            return Json(new { ok = true, duplicate = true, logId = log.LogId });

        var evidenceKey = await _evidenceStore.SaveUploadAsync(
            log.Student?.CandidateNumber ?? log.StudentId,
            log.ExamId ?? 0,
            log.AttemptId!.Value,
            request.EvidenceFile,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(evidenceKey))
        {
            return BadRequest(new
            {
                ok = false,
                message = "The clip was rejected. Only a valid, size-limited WebM event clip is accepted."
            });
        }

        if (log.ScreenshotPath is null && !string.IsNullOrWhiteSpace(request.ScreenshotDataUrl))
            log.ScreenshotPath ??= await _evidenceStore.SaveDataUrlAsync(log.Student?.CandidateNumber ?? log.StudentId, log.ExamId ?? 0, log.AttemptId.Value, request.ScreenshotDataUrl, cancellationToken);
        var oldEvidenceKey = log.VideoEvidencePath;
        log.VideoEvidencePath = evidenceKey;
        log.RecordingIncludesAudio = request.IncludesAudio;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _evidenceStore.DeleteAsync(evidenceKey, cancellationToken);
            throw;
        }

        if (!string.Equals(oldEvidenceKey, evidenceKey, StringComparison.Ordinal))
            await _evidenceStore.DeleteAsync(oldEvidenceKey, cancellationToken);

        return Json(new { ok = true, logId = log.LogId });
    }

    [HttpGet]
    [Route("/Student/Exam/Blocked")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Blocked(int attemptId, CancellationToken cancellationToken)
    {
        if (StudentId is null)
            return Challenge();

        // No hold created by the removed legacy decision engine may remain
        // active while the clean AI reset baseline is selected.
        var releasedSessionId = await TryReleaseLegacyHoldWhenAiDisabledAsync(
            attemptId,
            StudentId,
            cancellationToken);
        if (releasedSessionId.HasValue)
        {
            TempData["Success"] = "The legacy AI hold was removed and your paused examination time was restored.";
            return RedirectToAction(nameof(Session), new { sessionAttemptId = releasedSessionId.Value });
        }

        var attempt = await _context.ExamAttempts.AsNoTracking()
            .Include(x => x.Exam)
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.StudentId == StudentId);
        if (attempt?.Exam is null)
            return NotFound();

        if (attempt.Status != AttemptStatus.ProctoringBlocked)
        {
            var activeSessionId = await _context.ExamSessionAttempts.AsNoTracking()
                .Where(x => x.AttemptId == attemptId && x.Status == AttemptStatus.InProgress)
                .Select(x => (int?)x.SessionAttemptId)
                .FirstOrDefaultAsync();
            return activeSessionId.HasValue
                ? RedirectToAction(nameof(Session), new { sessionAttemptId = activeSessionId.Value })
                : RedirectToAction(nameof(Continue), new { attemptId });
        }

        var blockLog = await _context.ProctoringLogs.AsNoTracking()
            .Where(x => x.AttemptId == attemptId && x.AlertType == ProctoringAlertType.ExamBlocked)
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.LogId)
            .FirstOrDefaultAsync();

        return View(await BuildBlockedAuditAsync(attempt, blockLog, cancellationToken));
    }

    // Disabling a model must never authorize release of a persisted lock.
    private Task<int?> TryReleaseLegacyHoldWhenAiDisabledAsync(int attemptId, string studentId, CancellationToken cancellationToken)
        => Task.FromResult<int?>(null);

    [HttpGet]
    [Route("/Student/Exam/ProctoringStatus")]
    public async Task<IActionResult> ProctoringStatus(int attemptId)
    {
        if (StudentId is null)
            return Unauthorized();

        if (!_proctoringOptions.Enabled)
        {
            await TryReleaseLegacyHoldWhenAiDisabledAsync(
                attemptId,
                StudentId,
                HttpContext.RequestAborted);
        }

        var attempt = await _context.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.StudentId == StudentId);
        if (attempt is null)
            return NotFound();

        var sessionAttemptId = await _context.ExamSessionAttempts.AsNoTracking()
            .Where(x =>
                x.AttemptId == attemptId &&
                (x.Status == AttemptStatus.InProgress ||
                 x.Status == AttemptStatus.ProctoringBlocked))
            .OrderByDescending(x => x.StartedAt)
            .Select(x => (int?)x.SessionAttemptId)
            .FirstOrDefaultAsync();

        var blocked = attempt.Status == AttemptStatus.ProctoringBlocked;
        var identityRequired = !blocked && sessionAttemptId.HasValue &&
            !await HasRecentIdentityAsync(sessionAttemptId.Value);
        return Json(new
        {
            ok = true,
            blocked,
            identityRequired,
            attemptStatus = attempt.Status.ToString(),
            redirectUrl = blocked
                ? Url.Action(nameof(Blocked), new { attemptId })
                : sessionAttemptId.HasValue
                    ? Url.Action(nameof(Session), new { sessionAttemptId = sessionAttemptId.Value })
                    : null
        });
    }

    [HttpGet]
    [Route("/Student/Exam/Break")]
    public async Task<IActionResult> Break(int attemptId)
    {
        if (StudentId is null)
            return Challenge();

        var attempt = await _context.ExamAttempts
            .Include(x => x.Exam)
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.StudentId == StudentId);

        if (attempt?.Exam is null)
            return NotFound();

        if (attempt.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId });

        if (attempt.Status != AttemptStatus.InProgress)
            return RedirectToAction(nameof(AttemptResult), new { attemptId });

        var active = await _context.ExamSessionAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.Status == AttemptStatus.InProgress);

        // A next session may already have been created by the server lifecycle
        // after the maximum rest period. Resume it instead of creating a duplicate.
        if (active is not null)
            return RedirectToAction(nameof(Session), new { sessionAttemptId = active.SessionAttemptId });

        var next = await NextSession(attemptId);
        if (next is null)
        {
            await FinalizeAttempt(attempt);
            return RedirectToAction(nameof(AttemptResult), new { attemptId });
        }

        var previous = await _context.ExamSessionAttempts
            .AsNoTracking()
            .Include(x => x.Session)
            .Where(x =>
                x.AttemptId == attemptId &&
                (x.Status == AttemptStatus.Completed ||
                 x.Status == AttemptStatus.AutoSubmitted) &&
                x.CompletedAt != null)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync();

        if (previous?.Session is null)
            return RedirectToAction(nameof(Continue), new { attemptId });

        // Generated examinations use 10 minutes between sessions. Clamp any
        // administrative value to the supported real-world maximum.
        const int maximumRestMinutes = 10;

        var restEndsAt = previous.CompletedAt!.Value
            .AddMinutes(maximumRestMinutes);

        // If the maximum rest already ended, Continue will use/resume the
        // server-created next session immediately.
        if (DateTime.Now >= restEndsAt)
            return RedirectToAction(nameof(Continue), new { attemptId });

        return View("PartChoice", new SessionBreakViewModel
        {
            AttemptId = attemptId,
            ExamTitle = attempt.Exam.ExamTitle,
            CompletedSessionNumber = previous.Session.SessionNumber,
            TotalSessions = attempt.Exam.NumberOfSessions,
            NextSessionNumber = next.SessionNumber,
            NextSessionTitle = next.SessionTitle,
            NextSessionDurationMinutes = next.DurationMinutes,
            NextSessionTotalMarks = await _context.ExamQuestions.Where(q => q.SessionId == next.SessionId && q.IsActive).SumAsync(q => q.Marks),
            RestEndsAt = restEndsAt,
            MaximumRestMinutes = maximumRestMinutes
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/StartNextSession")]
    public async Task<IActionResult> StartNextSession(int attemptId)
    {
        if (StudentId is null)
            return Challenge();

        var attempt = await _context.ExamAttempts
            .Include(x => x.Exam)
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.StudentId == StudentId);

        if (attempt?.Exam is null)
            return NotFound();

        if (attempt.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId });

        if (attempt.Status != AttemptStatus.InProgress)
            return RedirectToAction(nameof(AttemptResult), new { attemptId });

        var active = await _context.ExamSessionAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.AttemptId == attemptId &&
                x.Status == AttemptStatus.InProgress);

        if (active is not null)
            return RedirectToAction(nameof(Session), new { sessionAttemptId = active.SessionAttemptId });

        var next = await NextSession(attemptId);
        if (next is null)
        {
            await FinalizeAttempt(attempt);
            return RedirectToAction(nameof(AttemptResult), new { attemptId });
        }

        // The candidate is explicitly allowed to end the optional break early.
        // The timer for the next session starts only now, on the server.
        try
        {
            var sessionAttempt = await CreateSessionAttempt(attempt, next);
            return RedirectToAction(nameof(Session), new { sessionAttemptId = sessionAttempt.SessionAttemptId });
        }
        catch (DbUpdateException)
        {
            // Protect against a race with the lifecycle worker or a double click.
            _context.ChangeTracker.Clear();

            var existing = await _context.ExamSessionAttempts
                .AsNoTracking()
                .Where(x =>
                    x.AttemptId == attemptId &&
                    x.SessionId == next.SessionId)
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync();

            if (existing is not null)
                return RedirectToAction(nameof(Session), new { sessionAttemptId = existing.SessionAttemptId });

            throw;
        }
    }

    [HttpGet]
    [Route("/Student/Exam/Continue")]
    public async Task<IActionResult> Continue(int attemptId)
    {
        if (StudentId is null) return Challenge();

        var attempt = await _context.ExamAttempts
            .Include(x => x.Exam)
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.StudentId == StudentId);
        if (attempt?.Exam is null) return NotFound();

        if (attempt.Status == AttemptStatus.ProctoringBlocked)
            return RedirectToAction(nameof(Blocked), new { attemptId });

        if (attempt.Status != AttemptStatus.InProgress)
            return RedirectToAction(nameof(AttemptResult), new { attemptId });

        var active = await _context.ExamSessionAttempts
            .Where(x => x.AttemptId == attemptId && x.Status == AttemptStatus.InProgress)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync();
        if (active is not null)
            return RedirectToAction(nameof(Session), new { sessionAttemptId = active.SessionAttemptId });

        var next = await NextSession(attemptId);
        if (next is null)
        {
            await FinalizeAttempt(attempt);
            return RedirectToAction(nameof(AttemptResult), new { attemptId });
        }

        // Enforce the break on the server. The countdown shown in the browser is only
        // a display; the next SessionAttempt (and therefore its timer) is not created
        // until this server-side rest period has finished.
        var previous = await _context.ExamSessionAttempts
            .AsNoTracking()
            .Include(x => x.Session)
            .Where(x => x.AttemptId == attemptId &&
                        (x.Status == AttemptStatus.Completed || x.Status == AttemptStatus.AutoSubmitted) &&
                        x.CompletedAt != null)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync();

        if (previous?.Session is not null)
        {
            const int restMinutes = 10;
            var restEndsAt = previous.CompletedAt!.Value.AddMinutes(restMinutes);
            if (restMinutes > 0 && DateTime.Now < restEndsAt)
            {
                var completedNumber = previous.Session.SessionNumber;
                return View("PartChoice", new SessionBreakViewModel
                {
                    AttemptId = attemptId,
                    ExamTitle = attempt.Exam.ExamTitle,
                    CompletedSessionNumber = completedNumber,
                    TotalSessions = attempt.Exam.NumberOfSessions,
                    NextSessionNumber = next.SessionNumber,
                    NextSessionTitle = next.SessionTitle,
                    NextSessionDurationMinutes = next.DurationMinutes,
                    NextSessionTotalMarks = await _context.ExamQuestions.Where(q => q.SessionId == next.SessionId && q.IsActive).SumAsync(q => q.Marks),
                    RestEndsAt = restEndsAt,
                    MaximumRestMinutes = restMinutes
                });
            }
        }

        var sessionAttempt = await CreateSessionAttempt(attempt, next);
        return RedirectToAction(nameof(Session), new { sessionAttemptId = sessionAttempt.SessionAttemptId });
    }

    [HttpGet]
    [Route("/Student/Exam/AttemptResult")]
    public async Task<IActionResult> AttemptResult(int attemptId)
    {
        if (StudentId is null) return Challenge();
        var result = await _context.ExamResults.AsNoTracking().Where(r => r.Attempt!.ResultReleasedAtUtc != null || (!r.Attempt.SubmittedForViolations && r.Attempt.Status != AttemptStatus.ProctoringBlocked && !_context.ProctoringLogs.Any(l => l.AttemptId == r.AttemptId && l.CountsAsViolation))).Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.Attempt!.StudentId == StudentId);
        if (result?.Attempt == null)
        {
            if (await _context.ExamAttempts.AnyAsync(a => a.AttemptId == attemptId && a.StudentId == StudentId && a.Status == AttemptStatus.Completed)) return View("ResultPending");
            return NotFound();
        }
        ViewBag.ExamRollNumber = await _context.ExamAssignments.AsNoTracking()
            .Where(x => x.ExamId == result.Attempt.ExamId && x.StudentId == StudentId && x.IsActive)
            .Select(x => x.RollNumber)
            .FirstOrDefaultAsync();
        return View(result);
    }

    private bool HasStoredProfilePhoto(ApplicationUser student)
    {
        if (string.IsNullOrWhiteSpace(student.ProfileImage) ||
            !student.ProfileImage.StartsWith("/ProfileImage/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(student.ProfileImage);
        if (string.IsNullOrWhiteSpace(fileName) || fileName != student.ProfileImage["/ProfileImage/".Length..])
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png")
            return false;

        var path = Path.Combine(AiExamProctoring.Services.Web.PrivateStorage.Root(_environment), "profiles", fileName);
        return System.IO.File.Exists(path);
    }

    private async Task<List<Question>> GetSessionQuestionsAsync(ExamSessionAttempt sa)
    {
        var rows = await _context.ExamQuestions
            .AsNoTracking()
            .Include(x => x.Question)
            .Where(x => x.ExamId == sa.Attempt!.ExamId &&
                        x.SessionId == sa.SessionId &&
                        x.IsActive &&
                        x.Question!.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        // Every candidate receives a stable, candidate-specific permutation.
        // The order survives refresh/resume, but a classmate's attempt uses a
        // different seed. The same question paper and marks are preserved.
        var seedMaterial = $"candidate-order-v2|{sa.AttemptId}|{sa.SessionId}|{sa.SessionAttemptId}";
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
        var rng = new Random(BitConverter.ToInt32(seedBytes, 0));
        for (var index = rows.Count - 1; index > 0; index--)
        {
            var swapIndex = rng.Next(index + 1);
            (rows[index], rows[swapIndex]) = (rows[swapIndex], rows[index]);
        }

        var result = new List<Question>();
        foreach (var row in rows)
        {
            if (row.Question is null)
                continue;

            var question = CloneQuestionForExam(row.Question, row.Marks, row.NegativeMarks);

            if (sa.Attempt?.Exam?.ShuffleOptions == true)
                ShuffleOptions(question, unchecked(2719 + sa.SessionAttemptId * 47 + row.QuestionId));

            result.Add(question);
        }

        return result;
    }

    private async Task<Question?> GetSessionQuestionAsync(ExamSessionAttempt session, int questionId)
    {
        // Autosave needs one question, not a materialized and shuffled paper.
        var row = await _context.ExamQuestions.AsNoTracking()
            .Include(x => x.Question)
            .FirstOrDefaultAsync(x => x.ExamId == session.Attempt!.ExamId &&
                x.SessionId == session.SessionId && x.QuestionId == questionId &&
                x.IsActive && x.Question!.IsActive, HttpContext.RequestAborted);
        if (row?.Question is null) return null;
        var question = CloneQuestionForExam(row.Question, row.Marks, row.NegativeMarks);
        if (session.Attempt?.Exam?.ShuffleOptions == true)
            ShuffleOptions(question, unchecked(2719 + session.SessionAttemptId * 47 + questionId));
        return question;
    }

    private async Task EnsurePublicAssignmentAsync(Exam exam, string studentId)
    {
        if (exam.Audience != ExamAudience.Public) return;
        var existingAssignment = await _context.ExamAssignments
            .FirstOrDefaultAsync(x => x.ExamId == exam.ExamId && x.StudentId == studentId);
        if (existingAssignment is not null)
        {
            if (!existingAssignment.IsActive)
            {
                existingAssignment.IsActive = true;
                existingAssignment.AssignedAt = DateTime.Now;
                existingAssignment.AssignedBy = exam.CreatedBy;
                await _context.SaveChangesAsync();
            }
            return;
        }

        var compactStudentId = new string(studentId.Where(char.IsLetterOrDigit).ToArray());
        var suffix = compactStudentId.Length >= 8 ? compactStudentId[..8].ToUpperInvariant() : compactStudentId.ToUpperInvariant();
        _context.ExamAssignments.Add(new ExamAssignment
        {
            ExamId = exam.ExamId,
            StudentId = studentId,
            RollNumber = $"PUB-{exam.ExamId:000000}-{suffix}",
            AssignedAt = DateTime.Now,
            AssignedBy = exam.CreatedBy,
            IsActive = true
        });
        _context.StudentNotifications.Add(new StudentNotification
        {
            StudentId = studentId,
            ExamId = exam.ExamId,
            Title = "Public examination joined",
            Message = $"You joined {exam.ExamTitle}. Your public exam roll number has been generated.",
            Type = "PublicExamJoined",
            CreatedDate = DateTime.Now
        });
        await _context.SaveChangesAsync();
    }

    private static bool IsValidOption(Question question, string option)
    {
        return option switch
        {
            "A" => !string.IsNullOrWhiteSpace(question.OptionA),
            "B" => !string.IsNullOrWhiteSpace(question.OptionB),
            "C" => !string.IsNullOrWhiteSpace(question.OptionC),
            "D" => !string.IsNullOrWhiteSpace(question.OptionD),
            "E" => !string.IsNullOrWhiteSpace(question.OptionE),
            _ => false
        };
    }

    private static Question CloneQuestionForExam(Question source, decimal marks, decimal negativeMarks)
    {
        return new Question
        {
            QuestionId = source.QuestionId,
            SubjectId = source.SubjectId,
            Subject = source.Subject,
            QuestionText = source.QuestionText,
            ImagePath = source.ImagePath,
            Explanation = source.Explanation,
            OptionA = source.OptionA,
            OptionB = source.OptionB,
            OptionC = source.OptionC,
            OptionD = source.OptionD,
            OptionE = source.OptionE,
            CorrectAnswer = source.CorrectAnswer,
            Topic = source.Topic,
            Tags = source.Tags,
            DisplayOrder = source.DisplayOrder,
            IsActive = source.IsActive,
            IsVerified = source.IsVerified,
            IsLocked = source.IsLocked,
            CreatedDate = source.CreatedDate,
            UpdatedDate = source.UpdatedDate,
            CreatedBy = source.CreatedBy,
            UpdatedBy = source.UpdatedBy,
            ExamMarks = marks,
            ExamNegativeMarks = negativeMarks
        };
    }

    private static void ShuffleOptions(Question question, int seed)
    {
        var options = new List<(string Letter, string Text)>
        {
            ("A", question.OptionA),
            ("B", question.OptionB),
            ("C", question.OptionC),
            ("D", question.OptionD)
        };

        if (!string.IsNullOrWhiteSpace(question.OptionE))
            options.Add(("E", question.OptionE));

        var originalCorrectLetter = question.CorrectAnswer;

        var random = new Random(seed);
        for (var i = options.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        var correctIndex = options.FindIndex(x =>
            string.Equals(x.Letter, originalCorrectLetter, StringComparison.Ordinal));

        question.OptionA = options[0].Text;
        question.OptionB = options[1].Text;
        question.OptionC = options[2].Text;
        question.OptionD = options[3].Text;
        question.OptionE = options.Count > 4 ? options[4].Text : null;

        question.CorrectAnswer = correctIndex switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            4 => "E",
            _ => originalCorrectLetter
        };
    }

    private Task<ExamSessionAttempt?> FindActiveSessionAsync(int attemptId, int sessionAttemptId) =>
        _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .FirstOrDefaultAsync(x =>
                x.SessionAttemptId == sessionAttemptId &&
                x.AttemptId == attemptId &&
                x.Attempt!.StudentId == StudentId &&
                x.Status == AttemptStatus.InProgress &&
                x.Attempt.Status == AttemptStatus.InProgress);

    private static string BuildBehaviorSessionKey(string studentId, int attemptId, int sessionAttemptId)
    {
        var raw = Encoding.UTF8.GetBytes($"{studentId}:{attemptId}:{sessionAttemptId}");
        return $"m2-{Convert.ToHexString(SHA256.HashData(raw))}";
    }

    private static async Task<List<FaceIdentityImage>> ReadFaceFramesAsync(IReadOnlyCollection<IFormFile> frames, CancellationToken cancellationToken)
    {
        var images = new List<FaceIdentityImage>(frames.Count);
        foreach (var frame in frames)
        {
            if (frame.Length < 8_000 || frame.Length > 5 * 1024 * 1024) continue;
            await using var stream = new MemoryStream();
            await frame.CopyToAsync(stream, cancellationToken);
            var bytes = stream.ToArray();
            var jpeg = bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9;
            var png = bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            if (!jpeg && !png) continue;
            images.Add(new(frame.FileName, png ? "image/png" : "image/jpeg", bytes));
        }
        return images;
    }

    private async Task<ExamSessionAttempt> CreateSessionAttempt(ExamAttempt attempt, ExamSession session)
    {
        var sa = new ExamSessionAttempt { AttemptId = attempt.AttemptId, SessionId = session.SessionId, StartedAt = DateTime.Now, Status = AttemptStatus.InProgress, TotalMarks = session.TotalMarks };
        _context.ExamSessionAttempts.Add(sa); await _context.SaveChangesAsync(); return sa;
    }

    private async Task<ExamSession?> NextSession(int attemptId)
    {
        var attempt = await _context.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.AttemptId == attemptId);
        if (attempt is null) return null;
        var done = await _context.ExamSessionAttempts.Where(x => x.AttemptId == attemptId && (x.Status == AttemptStatus.Completed || x.Status == AttemptStatus.AutoSubmitted)).Select(x => x.SessionId).ToListAsync();
        return await _context.ExamSessions.Where(x => x.ExamId == attempt.ExamId && x.IsActive && !done.Contains(x.SessionId)).OrderBy(x => x.DisplayOrder).FirstOrDefaultAsync();
    }

    private async Task FinalizeSession(ExamSessionAttempt sa, List<Question> questions, bool auto)
    {
        if (sa.Status != AttemptStatus.InProgress) return;
        sa.Status = auto ? AttemptStatus.AutoSubmitted : AttemptStatus.Completed; sa.CompletedAt = DateTime.Now;
        if (sa.TotalMarks <= 0) sa.TotalMarks = questions.Sum(q => q.ExamMarks);
        await _context.SaveChangesAsync();
    }

    private async Task FinalizeAttempt(ExamAttempt attempt)
    {
        if (attempt.Status != AttemptStatus.InProgress) return;
        var sessions = await _context.ExamSessionAttempts.Where(x => x.AttemptId == attempt.AttemptId).ToListAsync();
        var obtained = sessions.Sum(x => x.MarksObtained); var total = attempt.Exam?.TotalMarks ?? sessions.Sum(x => x.TotalMarks);
        attempt.TotalMarksObtained = obtained; attempt.TotalMarks = total; attempt.Percentage = total <= 0 ? 0 : Math.Round(obtained / total * 100m, 2);
        attempt.IsPassed = attempt.Percentage >= (attempt.Exam?.PassingPercentage ?? 40m);
        attempt.Status = attempt.IsDisqualified ? AttemptStatus.Disqualified : AttemptStatus.Completed; attempt.CompletedAt = DateTime.Now;
        var result = await _context.ExamResults.FirstOrDefaultAsync(x => x.AttemptId == attempt.AttemptId);
        var correct = sessions.Sum(x => x.CorrectAnswers); var wrong = sessions.Sum(x => x.WrongAnswers); var skipped = sessions.Sum(x => x.SkippedAnswers);
        if (result is null) _context.ExamResults.Add(new ExamResult { AttemptId = attempt.AttemptId, TotalMarksObtained = obtained, TotalMarks = total, Percentage = attempt.Percentage, IsPassed = attempt.IsPassed, CorrectAnswers = correct, WrongAnswers = wrong, SkippedAnswers = skipped, CertificateNumber = $"AIE-{DateTime.Now:yyyyMMdd}-{attempt.AttemptId:000000}", GeneratedDate = DateTime.Now });
        else { result.TotalMarksObtained = obtained; result.TotalMarks = total; result.Percentage = attempt.Percentage; result.IsPassed = attempt.IsPassed; result.CorrectAnswers = correct; result.WrongAnswers = wrong; result.SkippedAnswers = skipped; result.CertificateNumber ??= $"AIE-{DateTime.Now:yyyyMMdd}-{attempt.AttemptId:000000}"; }
        await _context.SaveChangesAsync();
    }
}
