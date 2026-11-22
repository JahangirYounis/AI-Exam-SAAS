using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.ViewModels.Student;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.Services.BehaviorAnalysis;
using AiExamProctoring.Services.ObjectDetection;
using AiExamProctoring.Services.SpeechRecognition;
using AiExamProctoring.Services.Proctoring;
using StudentAttemptUiState = AiExamProctoring.Services.Exam.StudentPortalAttemptState;
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

            var attemptCandidates = await _context.ExamAttempts.AsNoTracking()
                .Include(a => a.Exam)
                .Include(a => a.SessionAttempts).ThenInclude(s => s.Session)
                .Where(a => a.StudentId == studentId && a.ResultReleasedAtUtc == null &&
                    (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked))
                .OrderByDescending(a => a.StartedAt).ToListAsync(cancellationToken);
            // Include timed-out in-progress records for a "Check status" action,
            // never label them as resumable before authoritative server reconciliation.
            ViewBag.ResumableAttempts = attemptCandidates;

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
                x.Assignments.Any(a => a.StudentId == StudentId && a.IsActive))
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

        var usedAttempts = await _context.ExamAttempts.AsNoTracking()
            .CountAsync(x => x.ExamId == examId && x.StudentId == StudentId);
        if (usedAttempts > 0 && !exam.AllowsRepeatAttempts ||
            exam.AllowsRepeatAttempts && usedAttempts >= Math.Max(1, exam.MaximumAttempts))
        {
            TempData["Error"] = "You have reached the allowed number of attempts for this examination.";
            return RedirectToAction(nameof(Available));
        }

        ViewBag.IsReleased = exam.IsPublished;
        if (!exam.IsReusable && DateTime.Now > exam.LatestStartDate)
        {
            TempData["Error"] = $"The entry window for this examination closed at {exam.LatestStartDate:dd MMM yyyy, hh:mm tt}.";
            return RedirectToAction(nameof(Available));
        }

        if (!InstructionsAccepted(examId)) return View("Instructions", exam);

        // Opening precheck must not mark examination notifications as read.

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
        if (!exam.IsPublished || exam.Status == ExamStatus.Cancelled ||
            (!exam.IsReusable && (exam.Status == ExamStatus.Completed || DateTime.Now > exam.LatestStartDate)))
            return Conflict(new { ok = false, message = "Examination entry is not available. Return to examinations." });
        var eligibleStudent = await _context.ApplicationUsers.AsNoTracking().AnyAsync(x =>
            x.Id == StudentId && x.IsApproved && !x.IsBlocked);
        if (!eligibleStudent) return Forbid();
        if (exam.Audience == ExamAudience.Private && !await _context.ExamAssignments.AsNoTracking().AnyAsync(x => x.ExamId == request.ExamId && x.StudentId == StudentId && x.IsActive))
            return StatusCode(403, new { message = "You are not assigned to this examination." });
        var images = await ReadFaceFramesAsync(request.Frames, HttpContext.RequestAborted);
        if (images.Count != _faceOptions.VerificationFrames)
            return BadRequest(new { ok = false, message = "One or more camera frames were invalid." });
        var result = await _faceIdentity.VerifyAsync(StudentId, images, HttpContext.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            var statusCode = result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity;
            return StatusCode(statusCode, new { ok = false, identityState = result.ServiceUnavailable ? "IDENTITY_VERIFICATION_UNAVAILABLE" : "FACE_TRACKING_UNCERTAIN", message = IdentityMessage.For(result.Data?.ReasonCode, result.Message), status = result.Data?.Status, reasonCode = result.Data?.ReasonCode });
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
    public async Task<IActionResult> Start(int examId, string? verificationTicket, string? displaySurface = null)
    {
        if (StudentId is null) return Challenge();
        if (!InstructionsAccepted(examId)) return RedirectToAction(nameof(PreCheck), new { examId });
        if (displaySurface != "monitor")
            return BadRequest(new { message = "Choose Entire Screen before starting the exam." });
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
        if (existingAttempts.Count > 0 && !exam.AllowsRepeatAttempts ||
            exam.AllowsRepeatAttempts && existingAttempts.Count >= Math.Max(1, exam.MaximumAttempts))
        {
            TempData["Error"] = "You have reached the allowed number of attempts for this examination.";
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
        if (enrolledPhoto?.Bytes is not { Length: > 0 and <= 5000000 })
        {
            TempData["Error"] = "Your saved identity photo could not be loaded. Please retry or ask your teacher to check your enrollment.";
            return RedirectToAction(nameof(PreCheck), new { examId });
        }
        var attemptPhoto = enrolledPhoto.Bytes;
        var entryLogId = 0;
        var duplicateAttemptDetected = false;
        var entryTicketAlreadyUsed = false;
        var verificationUseKey = "precheck:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verified.Nonce)))[..32];
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

            if (await _context.ProctoringLogs.AnyAsync(l => l.ClientEventId == verificationUseKey))
            {
                entryTicketAlreadyUsed = true;
                await tx.RollbackAsync();
                return;
            }

            var transactionAttempts = await _context.ExamAttempts
                .Where(x => x.StudentId == studentId && x.ExamId == examId)
                .Select(x => x.Status)
                .ToListAsync();
            if (transactionAttempts.Any(s => s is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked)
                || (transactionAttempts.Count > 0 && !exam.AllowsRepeatAttempts)
                || (exam.AllowsRepeatAttempts && transactionAttempts.Count >= Math.Max(1, exam.MaximumAttempts)))
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
            _context.AttemptIdentityPhotos.Add(new AttemptIdentityPhoto
            {
                AttemptId = attempt.AttemptId,
                ImageBytes = attemptPhoto,
                Sha256 = Convert.ToHexString(SHA256.HashData(attemptPhoto)),
                CapturedAtUtc = DateTime.UtcNow
            });
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
                ClientEventId = verificationUseKey
            };
            _context.ProctoringLogs.Add(identityLog);
            await _context.SaveChangesAsync();
            entryLogId = identityLog.LogId;

            await tx.CommitAsync();
        });

        if (entryTicketAlreadyUsed)
        {
            TempData["Error"] = "This verification was already used. Complete a fresh live identity check.";
            return RedirectToAction(nameof(PreCheck), new { examId });
        }
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
        var sessionUrl = Url.Action(nameof(Session), new { sessionAttemptId = createdSessionAttemptId, entryTicket });
        return Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
            ? Json(new { sessionUrl })
            : RedirectToAction(nameof(Session), new { sessionAttemptId = createdSessionAttemptId, entryTicket });
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

        // Result release and finalization always win over an old resume link.
        if (attempt.CompletedAt.HasValue || attempt.ResultReleasedAtUtc.HasValue || attempt.IsDisqualified ||
            attempt.Exam?.Status == ExamStatus.Cancelled || attempt.Exam?.Status == ExamStatus.Completed)
            return RedirectToAction(nameof(AttemptResult), new { attemptId });

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
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
            // Browser GET never closes a session without grading persisted answers.
            // The lifecycle helper applies the same attempt-row lock as SubmitSession.
            await AiExamProctoring.Services.Exam.ExamLifecycleService.AutoSubmitSessionAsync(
                _context, sa, DateTime.Now, HttpContext.RequestAborted);
            return RedirectToAction(nameof(Continue), new { attemptId = sa.AttemptId });
        }

        // Precheck approval does not grant access to the paper. A new,
        // session-bound verification is required on every entry or resume.
        if (!_verificationTickets.TryValidate(entryTicket, out var entry) ||
            entry!.StudentId != StudentId || entry.ExamId != sa.Attempt.ExamId ||
            !entry.Nonce.StartsWith($"session:{sessionAttemptId}:", StringComparison.Ordinal) ||
            !await HasRecentIdentityAsync(sessionAttemptId, entry.Nonce))
        {
            HttpContext.Items["ExamCaptureFrame"] = true;
            return View("IdentityEntry", sa);
        }

        var exam = sa.Attempt.Exam;
        var questions = await GetSessionQuestionsAsync(sa);
        if (questions.Count == 0) { TempData["Error"] = "This session has no active questions configured."; return RedirectToAction(nameof(Available)); }
        var resumeQuestionId = sa.LastQuestionId.HasValue && questions.Any(x => x.QuestionId == sa.LastQuestionId.Value)
            ? sa.LastQuestionId.Value
            : questions[0].QuestionId;
        // One read for saved answers and review flags; these must describe the same snapshot.
        var savedRows = await _context.StudentAnswers.AsNoTracking()
            .Where(a => a.SessionAttemptId == sessionAttemptId)
            .Select(a => new { a.QuestionId, a.SelectedOption, a.IsMarkedForReview })
            .ToListAsync(HttpContext.RequestAborted);
        var existing = savedRows.ToDictionary(a => a.QuestionId, a => a.SelectedOption);
        var reviewed = savedRows.Where(a => a.IsMarkedForReview).Select(a => a.QuestionId).ToList();
        var sessionIndex = await _context.ExamSessions.Where(x => x.ExamId == exam.ExamId && x.IsActive && x.DisplayOrder <= sa.Session.DisplayOrder).CountAsync();
        // Authoritative session time is server-side and starts only when this
        // session attempt is created. Rest between sessions never consumes it.
        var expires = (sa.StartedAt ?? DateTime.Now).AddMinutes(sa.Session.DurationMinutes);
        if (DateTime.Now >= expires)
        {
            // Browser GET never closes a session without grading persisted answers.
            // The lifecycle helper applies the same attempt-row lock as SubmitSession.
            await AiExamProctoring.Services.Exam.ExamLifecycleService.AutoSubmitSessionAsync(
                _context, sa, DateTime.Now, HttpContext.RequestAborted);
            return RedirectToAction(nameof(Continue), new { attemptId = sa.AttemptId });
        }
        ViewBag.ResumeQuestionId = resumeQuestionId;
        // Independent service health probes run concurrently to keep session entry responsive.
        var objectHealthTask = _objectDetection.HealthAsync(HttpContext.RequestAborted);
        var speechHealthTask = _speechRecognition.HealthAsync(HttpContext.RequestAborted);
        await Task.WhenAll(objectHealthTask, speechHealthTask);
        var objectHealth = await objectHealthTask;
        ViewBag.ObjectDetectionReady = objectHealth.Success && objectHealth.Data?.Ready == true;
        ViewBag.ObjectDetectionEnabled = _objectOptions.Enabled;
        ViewBag.ObjectDetectionMessage = objectHealth.Success
            ? objectHealth.Data?.Message ?? "Model 3 is ready."
            : objectHealth.Message;
        var speechHealth = await speechHealthTask;
        ViewBag.SpeechRecognitionReady = speechHealth.Success && speechHealth.Data?.Ready == true;
        ViewBag.SpeechRecognitionMessage = speechHealth.Success
            ? speechHealth.Data?.Message ?? "Model 4 is ready."
            : speechHealth.Message;
        HttpContext.Items["ExamCaptureFrame"] = true;
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

        // The browser cannot decide whether the authoritative server clock
        // has expired. A client-provided autoSubmitted flag is informational.

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
        var autoSubmitted = serverAutoSubmit;

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

        // Use one server-owned scoring implementation for manual submit, timeout,
        // and blocked-attempt review. This action already holds the attempt UPDLOCK
        // and transaction in SubmitSession().
        await AiExamProctoring.Services.Results.ExamResultFinalizationService.ScoreSessionAsync(
            _context, sa, sa.Attempt!.Exam!.ShuffleOptions, serverAutoSubmit ? null : answers,
            DateTime.Now, latestSavedAt: serverAutoSubmit ? expiry : null,
            ct: HttpContext.RequestAborted);
        await _context.SaveChangesAsync(HttpContext.RequestAborted);

        await FinalizeSession(sa, questions, autoSubmitted);

        if (await NextSession(sa.AttemptId) is null)
        {
            await FinalizeAttempt(sa.Attempt, callerHoldsAttemptLock: true);
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
        if (session.Attempt?.Status == AttemptStatus.Completed)
            return RedirectToAction(nameof(AttemptResult), new { attemptId = session.AttemptId });
        return View(session);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Route("/Student/Exam/SaveAnswer")]
    public async Task<IActionResult> SaveAnswer(int sessionAttemptId, int questionId, string? selectedOption)
    {
        if (StudentId is null) return Unauthorized();
        var attemptId = await _context.ExamSessionAttempts.AsNoTracking()
            .Where(s => s.SessionAttemptId == sessionAttemptId && s.Attempt!.StudentId == StudentId)
            .Select(s => (int?)s.AttemptId).SingleOrDefaultAsync(HttpContext.RequestAborted);
        if (attemptId == null) return NotFound();
        // MVC ExamWriteGuard already owns this transaction; direct callers use the same lock below.
        if (_context.Database.CurrentTransaction != null) return await SaveAnswerCore(sessionAttemptId, questionId, selectedOption);
        return await Services.Results.ExamResultFinalizationService.WriteAsync(_context, attemptId.Value,
            () => SaveAnswerCore(sessionAttemptId, questionId, selectedOption), HttpContext.RequestAborted);
    }

    private async Task<IActionResult> SaveAnswerCore(int sessionAttemptId, int questionId, string? selectedOption)
    {
        if (StudentId is null) return Unauthorized();
        var sa = await _context.ExamSessionAttempts
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .Include(x => x.Session)
            .FirstOrDefaultAsync(x => x.SessionAttemptId == sessionAttemptId && x.Attempt!.StudentId == StudentId);
        if (sa is null || sa.Session is null || sa.FrozenAtUtc.HasValue || sa.Status != AttemptStatus.InProgress ||
            sa.Attempt?.Status != AttemptStatus.InProgress || sa.Attempt.CompletedAt.HasValue || sa.Attempt.ResultReleasedAtUtc.HasValue)
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
        // Forward-only examinations must enforce question order on the server too.
        // Use the same stable permutation as the page, once per write.
        var orderedQuestions = await GetSessionQuestionsAsync(sa);
        var submittedIndex = orderedQuestions.FindIndex(q => q.QuestionId == questionId);
        if (submittedIndex < 0 || !sa.Attempt.Exam!.AllowBackNavigation &&
            submittedIndex != sa.CurrentQuestionIndex)
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This question cannot be edited after advancing." });
        var option = string.IsNullOrWhiteSpace(selectedOption) ? null : selectedOption.Trim().ToUpperInvariant();
        if (option is not null && !IsValidOption(currentQuestion, option))
            return BadRequest(new { ok = false, message = "Invalid option for this question." });
        var answer = await _context.StudentAnswers.FirstOrDefaultAsync(a => a.SessionAttemptId == sessionAttemptId && a.QuestionId == questionId);
        if (answer is null) { answer = new StudentAnswer { SessionAttemptId = sessionAttemptId, QuestionId = questionId }; _context.StudentAnswers.Add(answer); }
        answer.SelectedOption = option; answer.AnsweredAt = DateTime.Now; answer.IsCorrect = null; answer.MarksAwarded = 0;
        sa.LastQuestionId = questionId;
        sa.CurrentQuestionIndex = submittedIndex;
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
            sa.Attempt?.Status != AttemptStatus.InProgress || sa.Attempt.CompletedAt.HasValue ||
            sa.Attempt.ResultReleasedAtUtc.HasValue)
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
        if (!sa.Attempt.Exam!.AllowBackNavigation)
        {
            var questionsInOrder = await GetSessionQuestionsAsync(sa);
            if (questionsInOrder.FindIndex(q => q.QuestionId == questionId) != sa.CurrentQuestionIndex)
                return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "This question is not the active question in a forward-only exam." });
        }
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
            sessionAttempt.FrozenAtUtc.HasValue ||
            sessionAttempt.Attempt.CompletedAt.HasValue || sessionAttempt.Attempt.ResultReleasedAtUtc.HasValue ||
            sessionAttempt.Attempt.Status != AttemptStatus.InProgress)
        {
            return BadRequest(new { ok = false });
        }

        var belongsToSession = await _context.ExamQuestions.AsNoTracking().AnyAsync(x =>
            x.ExamId == sessionAttempt.Attempt.ExamId && x.SessionId == sessionAttempt.SessionId &&
            x.QuestionId == questionId && x.IsActive && x.Question!.IsActive, HttpContext.RequestAborted);
        if (!belongsToSession) return BadRequest(new { ok = false });
        if (DateTime.Now >= (sessionAttempt.StartedAt ?? DateTime.Now).AddMinutes(sessionAttempt.Session.DurationMinutes))
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, expired = true });
        if (!sessionAttempt.Attempt.Exam.AllowBackNavigation && sessionAttempt.LastQuestionId.HasValue)
        {
            var ordered = await GetSessionQuestionsAsync(sessionAttempt);
            var destinationIndex = ordered.FindIndex(q => q.QuestionId == questionId);
            if (destinationIndex < sessionAttempt.CurrentQuestionIndex ||
                destinationIndex > sessionAttempt.CurrentQuestionIndex + 1)
                return StatusCode(StatusCodes.Status409Conflict, new { ok = false, message = "Only the next question is available in a forward-only examination." });
        }

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

        if (alertType == ProctoringAlertType.TabSwitch) { alertType = ProctoringAlertType.ExamPageHidden; severity = ProctoringSeverity.Low; request.Description = "The exam page became hidden. The specific browser action could not be determined."; }

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
            // The legacy form has no fullscreen episode lifecycle. Only BrowserObservation can submit a qualifying exit.
            PolicyProcessed = alertType == ProctoringAlertType.FullScreenExit,
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
    public async Task<IActionResult> VerifyActiveIdentity([FromForm] FaceIdentityFrameRequest request, [FromForm] Guid? identitySequenceId = null)
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

        // A compact, valid JPEG can be smaller than the enrollment quality floor.
        // The identity worker still decodes and quality-checks every frame.
        var images = await ReadFaceFramesAsync(request.Frames, HttpContext.RequestAborted, minimumBytes: 4);
        if (images.Count != _faceOptions.ContinuousVerificationFrames)
            return BadRequest(new { ok = false, message = "One or more camera frames were invalid." });
        var result = await _faceIdentity.VerifyActiveAsync(StudentId, images, HttpContext.RequestAborted);
        // Another model can lock the attempt while identity inference is in flight.
        var currentState = await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted);
        if (currentState?.Status == nameof(AttemptStatus.ProctoringBlocked))
            return StatusCode(423, new { ok = false, blocked = true, state = currentState });
        var presentationUnconfirmed = result.Data?.ReasonCode == "presentation_unconfirmed";
        if ((!result.Success && !presentationUnconfirmed) || result.Data is null)
        {
            await RecordIdentityUncertaintyAsync(active, result.Data?.ReasonCode ?? "service_unavailable", result.Message, observationStartedAt);
            return StatusCode(result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, identityState = result.ServiceUnavailable ? "IDENTITY_VERIFICATION_UNAVAILABLE" : "FACE_TRACKING_UNCERTAIN", message = IdentityMessage.For(result.Data?.ReasonCode, result.Message), status = result.Data?.Status, reasonCode = result.Data?.ReasonCode });
        }

        // Only a fully usable, explicitly rejected gallery comparison may start
        // a substitution confirmation sequence. Borderline scores, poor lighting,
        // PAD uncertainty and service errors are technical/review observations.
        // No hard-coded similarity cutoff may override the Python model's
        // configured, still-provisional gallery calibration.
        var criticalMismatch = result.Success &&
            result.Data.FrameCount >= _faceOptions.ContinuousVerificationFrames &&
            result.Data.ReferenceCount >= 5 && !presentationUnconfirmed &&
            !result.Data.Verified && result.Data.Status == "rejected" &&
            result.Data.ReasonCode == "identity_mismatch";
        string? checkEvidenceKey = null;
        if (_proctoringOptions.CaptureEvidence && result.Data.Verified && !await _context.ProctoringLogs.AnyAsync(l => l.AttemptId == active.AttemptId && l.AlertType == ProctoringAlertType.IdentityVerified && l.ScreenshotPath != null, HttpContext.RequestAborted))
        {
            var original = images[^1];
            var prefix = original.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? "data:image/png;base64," : "data:image/jpeg;base64,";
            try
            {
                checkEvidenceKey = await _evidenceStore.SaveDataUrlAsync(StudentId, active.Attempt.ExamId, active.AttemptId,
                prefix + Convert.ToBase64String(original.Bytes), HttpContext.RequestAborted);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                HttpContext.RequestServices.GetService<ILogger<ExamController>>()?.LogWarning(error, "Identity check evidence unavailable for attempt {AttemptId}", active.AttemptId);
            }
        }
        int? confirmedSampleId = null;
        DateTime? confirmationStartedAt = null;
        if (criticalMismatch)
        {
            var previousDecision = await _context.ProctoringLogs.AsNoTracking()
                .Where(l => l.AttemptId == active.AttemptId && l.SessionAttemptId == active.SessionAttemptId &&
                    (l.AlertType == ProctoringAlertType.IdentityVerified || l.AlertType == ProctoringAlertType.IdentityMismatch && l.Severity == ProctoringSeverity.High))
                .OrderByDescending(l => l.LogId).FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (previousDecision?.AlertType == ProctoringAlertType.IdentityMismatch &&
                (!active.Attempt.TeacherResumedAtUtc.HasValue || previousDecision.FrameCapturedAt?.ToUniversalTime() > active.Attempt.TeacherResumedAtUtc))
                return Json(new
                {
                    ok = true,
                    verified = false,
                    identityState = "MANUAL_IDENTITY_REVIEW_REQUIRED",
                    message = "Candidate identity change suspected. Teacher review is already pending.",
                    logId = previousDecision.LogId
                });
            var frameHash = string.Join(":", images.Select(image => Convert.ToHexString(SHA256.HashData(image.Bytes))[..16]).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            var confirmation = await ConfirmIdentityMismatchAsync(active.AttemptId, active.SessionAttemptId,
                observationStartedAt, frameHash, result.Data.MedianSimilarity, HttpContext.RequestAborted, checkEvidenceKey, identitySequenceId);
            confirmedSampleId = confirmation.ConfirmedSampleId;
            confirmationStartedAt = confirmation.StartedAt;
            if (!confirmedSampleId.HasValue)
                return Json(new
                {
                    ok = true,
                    verified = false,
                    confirmationPending = true,
                    identityState = "POSSIBLE_IDENTITY_MISMATCH",
                    retryAfterMilliseconds = IdentityRecheckPolicy.SuggestedIntervalSeconds * 1000,
                    confirmedMismatchBatches = confirmation.Count,
                    requiredMismatchBatches = IdentityRecheckPolicy.RequiredMismatchBatches,
                    messageCode = "FRESH_IDENTITY_RECHECK_PENDING",
                    message = "Checking identity again. Please face the camera while fresh frames are compared."
                });
        }
        var isVerified = result.Data.Verified && result.Data.FrameCount >= _faceOptions.ContinuousVerificationFrames && !presentationUnconfirmed && !criticalMismatch;
        // Uncertain presentation and borderline similarity need a fresh sample;
        // neither is evidence that the candidate has been replaced.
        if (!isVerified && !criticalMismatch)
        {
            await RecordIdentityUncertaintyAsync(active, result.Data.ReasonCode ?? "identity_uncertain", result.Data.Message ?? result.Message, observationStartedAt);
            return UnprocessableEntity(new
            {
                ok = false,
                verified = false,
                retry = true,
                identityState = "REVERIFICATION_REQUIRED",
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
        string? identityEvidenceKey = checkEvidenceKey;
        if (criticalMismatch && identityEvidenceKey is null && _proctoringOptions.CaptureEvidence)
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
            Description = presentationUnconfirmed ? "Camera presentation could not be authenticated. Fresh verification is required; this is not a misconduct finding." : (isVerified ? "Registered candidate verified against the authorized reference." : "Student Changed: the initial check and one fresh recheck did not match the registered candidate. Examination held for teacher review."),
            ScreenshotPath = identityEvidenceKey,
            ConfidenceScore = result.Data.MedianSimilarity,
            DetectedFaceCount = 1,
            ModelName = _faceOptions.ModelName,
            ModelVersion = result.Data.ModuleVersion ?? _faceOptions.ModelVersion,
            ClientEventId = Guid.NewGuid().ToString("N"),
            FrameCapturedAt = observationStartedAt,
            IncidentStartedAtUtc = criticalMismatch ? confirmationStartedAt?.ToUniversalTime() ?? observationStartedAt.ToUniversalTime() : null,
            RecoveryStatus = criticalMismatch ? "UnderReview" : null,
            CountsAsViolation = false,
            // Only a confirmed, fresh-batch sequence can trigger the identity hold.
            MachineObservationsJson = criticalMismatch
                ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    identityConfirmation = IdentityRecheckPolicy.ConfirmationMarker,
                    confirmedBatches = IdentityRecheckPolicy.RequiredMismatchBatches,
                    requiresTeacherReview = true,
                    notProofOfMisconduct = true
                }) : null,
            PolicyPending = criticalMismatch,
            IsReviewed = isVerified,
            CreatedDate = DateTime.Now
        };
        var decisionSaved = await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var transaction = await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            await AiExamProctoring.Services.Proctoring.ProctoringStateService.LockAttemptAsync(_context, active.AttemptId, HttpContext.RequestAborted);
            if (!await IsCurrentIdentityObservationAsync(active.AttemptId, active.SessionAttemptId,
                observationStartedAt, HttpContext.RequestAborted)) return false;
            var latest = await _context.ProctoringLogs.AsNoTracking().Where(l =>
                l.AttemptId == active.AttemptId && l.SessionAttemptId == active.SessionAttemptId &&
                (l.AlertType == ProctoringAlertType.IdentityVerified || l.AlertType == ProctoringAlertType.IdentityMismatch ||
                 l.ModelName == "Identity monitoring health"))
                .OrderByDescending(l => l.FrameCapturedAt ?? l.CreatedDate).ThenByDescending(l => l.LogId)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            // An older successful inference must not erase a newer identity concern.
            if (latest is not null && (latest.FrameCapturedAt ?? latest.CreatedDate).ToUniversalTime() >
                observationStartedAt.ToUniversalTime()) return false;
            if (criticalMismatch && latest?.LogId != confirmedSampleId) return false;
            var liveAttempt = await _context.ExamAttempts.AsNoTracking().SingleAsync(a => a.AttemptId == active.AttemptId, HttpContext.RequestAborted);
            if (liveAttempt.Status != AttemptStatus.InProgress || liveAttempt.TeacherResumedAtUtc > observationStartedAt.ToUniversalTime()) return false;
            if (criticalMismatch && latest?.AlertType == ProctoringAlertType.IdentityMismatch && latest.Severity == ProctoringSeverity.High) return false;
            _context.ProctoringLogs.Add(log);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
            return true;
        });
        if (!decisionSaved) return Conflict(new { ok = false, identityState = "REVERIFICATION_REQUIRED", message = "Identity state changed while checking. Please retry with fresh frames." });
        if (criticalMismatch) await _states.ApplyLogAsync(log.LogId, HttpContext.RequestAborted);
        if (criticalMismatch)
        {
            var confirmedState = await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted);
            if (confirmedState?.Status == nameof(AttemptStatus.ProctoringBlocked))
                return StatusCode(423, new { ok = false, blocked = true, state = confirmedState, logIds = new[] { log.LogId } });
            return Json(new
            {
                ok = true,
                verified = false,
                identityMismatchConfirmed = true,
                logIds = new[] { log.LogId },
                identityState = "REPEATED_IDENTITY_MISMATCH",
                message = "Candidate identity change suspected. Repeated checks require urgent teacher review.",
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
            identityState = isVerified ? "REGISTERED_CANDIDATE_VERIFIED" : "MANUAL_IDENTITY_REVIEW_REQUIRED",
            entryTicket,
            candidateName = await _context.Users.Where(x => x.Id == StudentId)
                .Select(x => x.FullName).FirstOrDefaultAsync() ?? "the registered candidate",
            identityRequired = !isVerified,
            redirectUrl = Url.Action(nameof(Session), new { sessionAttemptId = active.SessionAttemptId }),
            status = result.Data.Status,
            observationOnly = true,
            blocked = criticalMismatch,
            logId = log.LogId
        });
    }

    private async Task RecordIdentityUncertaintyAsync(ExamSessionAttempt active, string reason, string message, DateTime capturedAt)
    {
        // A technical/quality problem must remain visible to the owning teacher,
        // but must never be processed as a strike or an identity mismatch.
        var latest = await _context.ProctoringLogs.Where(x => x.SessionAttemptId == active.SessionAttemptId &&
            (x.AlertType == ProctoringAlertType.IdentityVerified || x.AlertType == ProctoringAlertType.IdentityMismatch || x.ModelName == "Identity monitoring health"))
            .OrderByDescending(x => x.LogId).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (latest?.ModelName == "Identity monitoring health" && latest.CreatedDate >= DateTime.Now.AddMinutes(-1)) return;
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
            FrameCapturedAt = capturedAt,
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
        var lastReturn = await _context.ProctoringLogs.AsNoTracking()
            .Where(l => l.StudentId == StudentId && l.SessionAttemptId == sessionAttemptId &&
                l.IncidentKey == "absence" && l.IncidentEndedAtUtc != null)
            .MaxAsync(l => l.IncidentEndedAtUtc, HttpContext.RequestAborted);
        if (lastReturn is DateTime returnedAt && !await _context.ProctoringLogs.AsNoTracking().AnyAsync(l =>
            l.StudentId == StudentId && l.SessionAttemptId == sessionAttemptId &&
            l.AlertType == ProctoringAlertType.IdentityVerified && l.FrameCapturedAt > returnedAt.ToLocalTime(),
            HttpContext.RequestAborted)) return false;
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
        if (request.ScreenPoints is not (4 or 6)) return BadRequest();
        var expectedFrames = _behaviorOptions.BaselineFrames + (request.ScreenCalibration ? request.ScreenPoints * 2 : 0);
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
            HttpContext.RequestAborted, request.ScreenCalibration, request.ScreenPoints);
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
        var feature = request.Feature;
        string[] incidentKeys = feature switch
        {
            null => ["attention", "persons", "absence", "face_visibility", "mouth"],
            "attention" => ["attention", "face_visibility", "mouth"],
            "absence" => ["absence"],
            "persons" => ["persons"],
            _ => []
        };
        if (incidentKeys.Length == 0)
            return BadRequest(new { ok = false, message = "Unknown behavior detector." });

        var sessionKey = BuildBehaviorSessionKey(StudentId, request.AttemptId, request.SessionAttemptId);
        var workerStarted = System.Diagnostics.Stopwatch.StartNew();
        var result = feature is "absence" or "persons"
            ? await _behaviorAnalysis.AnalyzeFeatureAsync(feature, sessionKey, image, HttpContext.RequestAborted)
            : await _behaviorAnalysis.AnalyzeAsync(sessionKey, image, HttpContext.RequestAborted);
        var workerMs = workerStarted.Elapsed.TotalMilliseconds;
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

        if (result.Data.Signals is null ||
            (feature is not null && !result.Data.Signals.ContainsKey(feature)))
            return StatusCode(503, new { ok = false, message = "The selected detector did not return its incident signal." });
        var metrics = result.Data.Metrics;
        var presence = result.Data.PresenceStatus;
        var people = result.Data.PeopleStatus;
        // Never treat a failed person-detector inference as verified recovery.
        // The live/gaze lanes remain independent of this detector.
        if (people is null)
            result.Data.Signals["persons"] = null;
        // With no working ONNX body detector, stable distinct face regions may
        // justify an *uncertain Teacher review* but NEVER a supported multiple-
        // person cheating strike. PeopleContextSupported stays false without
        // independent body/liveness support and BlockingPolicy fails closed.
        else if (metrics?.PersonDetectorReady != true && people.ContextSupported)
            result.Data.Signals["persons"] = null;
        // Physical absence is a strike only after the independent person detector
        // has supplied at least three seconds of fresh, usable zero-person frames.
        var verifiedPhysicalAbsence = presence?.State == "VERIFIED_PHYSICAL_ABSENCE" &&
            presence.ElapsedSeconds >= Math.Max(3, _proctoringOptions.FaceAbsencePersistenceSeconds) &&
            result.Data.Capabilities?.PhysicalAbsence == true && metrics?.PersonDetectorReady == true &&
            metrics.PresenceOk == true;
        if (result.Data.Signals.GetValueOrDefault("absence") == true && !verifiedPhysicalAbsence)
            result.Data.Signals["absence"] = null;
        if (presence is null || metrics?.PersonDetectorReady != true)
            result.Data.Signals["absence"] = null;
        // A missing face is a review-only observation, not proof that the candidate
        // physically left. Confirm physical absence separately with the person detector.
        // Never turn an uncertain face-visibility observation into a cheating strike.
        // Physical return ends the absence episode independently of biometric identity.
        // Identity mismatch remains owned by the existing identity lane.
        if (feature is (null or "absence") && result.Data.Signals.GetValueOrDefault("absence") == false &&
            !AbsenceReturnPolicy.PhysicallyPresent(presence?.State, metrics?.PersonDetectorReady == true,
                metrics?.PresenceOk == true))
            result.Data.Signals["absence"] = null;
        // The worker owns the pre-confirmation timer. Provisional presence is
        // shared with the teacher without opening an incident or counting a warning.
        var provisionalAbsence = presence?.State == "POSSIBLE_ABSENCE" && metrics?.PersonDetectorReady == true &&
            metrics.PresenceOk == true && result.Data.Capabilities?.PhysicalAbsence == true;
        var attention = result.Data.AttentionStatus.GetValueOrDefault();
        var calibrated = attention.ValueKind == System.Text.Json.JsonValueKind.Object &&
            attention.TryGetProperty("calibrated", out var calibratedValue) &&
            calibratedValue.ValueKind == System.Text.Json.JsonValueKind.True;
        bool? attentionSignal = calibrated ? result.Data.Signals.GetValueOrDefault("attention") : null;
        var confirmedAttention = attentionSignal == true &&
            attention.TryGetProperty("state", out var attentionState) &&
            attentionState.ValueKind == System.Text.Json.JsonValueKind.String &&
            attentionState.ValueEquals("CONFIRMED_LOOKING_AWAY");
        double? ReadConfirmedDuration(string field) => confirmedAttention &&
            attention.TryGetProperty(field, out var duration) &&
            duration.ValueKind == System.Text.Json.JsonValueKind.Number && duration.TryGetDouble(out var seconds)
            ? seconds : null;
        var sustainedDuration = ReadConfirmedDuration("supported_duration_seconds");
        var repeatedDuration = ReadConfirmedDuration("repeated_duration_seconds");
        var repeatedLookingAway = repeatedDuration.HasValue && (!sustainedDuration.HasValue || repeatedDuration > sustainedDuration);
        var attentionDuration = repeatedLookingAway ? repeatedDuration : sustainedDuration;
        // Pending attention can neither confirm nor close an incident.
        if (attentionSignal == true && (!confirmedAttention || attentionDuration is not double supported ||
            !double.IsFinite(supported) || supported < Math.Max(2, _proctoringOptions.AttentionPersistenceSeconds)))
            attentionSignal = null;
        var observations = new List<IncidentObservation>();
        foreach (var key in incidentKeys)
        {
            var directionCode = result.Data.SignalCodes.GetValueOrDefault("attention") ?? "";
            var attentionIsHead = directionCode.StartsWith("HEAD_", StringComparison.Ordinal);
            var type = key == "persons" ? ProctoringAlertType.MultipleFaces : key == "absence"
                ? ProctoringAlertType.FaceNotDetected : key == "face_visibility"
                ? ProctoringAlertType.FaceNotVisible : key == "attention"
                ? attentionIsHead ? ProctoringAlertType.HeadAway : ProctoringAlertType.GazeAway
                : ProctoringAlertType.LookAway;
            observations.Add(new(key, key == "attention" ? attentionSignal : result.Data.Signals.GetValueOrDefault(key), new ProctoringLog
            {
                AlertType = type,
                Severity = key == "face_visibility" ? ProctoringSeverity.Low : key == "attention" ? ProctoringSeverity.Medium : ProctoringSeverity.High,
                Description = key == "attention" && repeatedLookingAway
                    ? "Repeated looks away from the calibrated examination screen."
                    : key == "attention" ? "Possible " + (attentionIsHead ? "head movement" : "eye/gaze deviation") +
                    " relative to the calibrated screen (" + (directionCode.Length <= 24 ? directionCode : "unknown") + "). Review in context." :
                    key == "persons" ? people?.ContextSupported == true
                        ? $"More than one person detected during the test ({people.LiveCount} supported). Review original camera video."
                        : $"Possible second person ({people?.ObservedCount ?? 0} face/body regions). Live-person support is incomplete; Teacher review only." :
                    key == "absence" ? "The candidate was physically absent from the camera view. Review the camera evidence." :
                    key == "face_visibility" ? "Candidate face not visible in usable camera frames. Physical absence or identity change is NOT confirmed; review the evidence." :
                    "Supporting mouth movement observation.",
                DetectedFaceCount = key == "persons" ? (people?.ObservedCount ?? metrics?.FaceCount) : metrics?.FaceCount,
                // A provisional face count is NOT the supported simultaneous-person count.
                MaxSimultaneousPeople = key == "persons" && people?.ContextSupported == true ? people.LiveCount : null,
                MachineObservationsJson = key == "absence" ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    source = "Model2Behavior",
                    physicalPresenceState = presence?.State,
                    personDetectorReady = metrics?.PersonDetectorReady,
                    cameraPresenceUsable = metrics?.PresenceOk,
                    modelElapsedSeconds = presence?.ElapsedSeconds,
                    confirmationRequiredSeconds = Math.Max(3, _proctoringOptions.FaceAbsencePersistenceSeconds)
                }) : key == "persons" && people is not null
                    ? System.Text.Json.JsonSerializer.Serialize(new { status = people, detectorReady = metrics?.PersonDetectorReady, inferenceMs = metrics?.PersonInferenceMs })
                    : key == "attention" && metrics is not null
                    ? System.Text.Json.JsonSerializer.Serialize(new { signalCode = directionCode, calibrated, supportedDurationSeconds = attentionDuration, repeatedGlances = repeatedLookingAway, metrics.Yaw, metrics.Pitch, metrics.Roll, metrics.GazeLeft, metrics.GazeRight, metrics.GazeUp, metrics.GazeDown, metrics.QualityOk, metrics.HeadPoseOk, metrics.FrameProcessingMs })
                    : null,
                ModelName = _behaviorOptions.ModelName,
                ModelVersion = result.Data.ModuleVersion ?? _behaviorOptions.ModelVersion
            }, key == "absence" && presence?.State is ("POSSIBLE_ABSENCE" or "VERIFIED_PHYSICAL_ABSENCE") ? presence.ElapsedSeconds
               : key == "persons" && people?.ContextSupported == true ? people.SupportedDurationSeconds
               : key == "attention" ? attentionDuration : null,
               key == "persons" && people?.ContextSupported == true,
               RepeatedAttention: key == "attention" && repeatedLookingAway,
               SupportedAttention: key == "attention" && attentionSignal == true,
               ProvisionalAbsence: key == "absence" && provisionalAbsence));
        }
        var logIds = await _states.ObserveAsync(active.AttemptId, active.SessionAttemptId, StudentId,
            observationStartedAt, observations, HttpContext.RequestAborted);
        var evidenceLogIds = logIds.Count == 0 ? Array.Empty<int>() : await _context.ProctoringLogs.AsNoTracking()
            .Where(l => logIds.Contains(l.LogId) && l.ParentIncidentId == null &&
                (l.IncidentKey != "persons" || l.AlertType == ProctoringAlertType.MultipleFaces))
            .Select(l => l.LogId).ToArrayAsync(HttpContext.RequestAborted);
        // Attention screenshot and clip are handled by the browser evidence queue.
        // Keep the existing server fallback for separate face-visibility records.
        if (feature == "attention" && evidenceLogIds.Length > 0)
        {
            var faceVisibilityIds = await _context.ProctoringLogs.AsNoTracking()
                .Where(l => evidenceLogIds.Contains(l.LogId) && l.IncidentKey == "face_visibility")
                .Select(l => l.LogId).ToArrayAsync(HttpContext.RequestAborted);
            await AttachObservationImageAsync(faceVisibilityIds, image, active.Attempt.ExamId, active.AttemptId);
        }
        else if (feature != "persons")
            await AttachObservationImageAsync(evidenceLogIds, image, active.Attempt.ExamId, active.AttemptId);
        var absenceIncident = feature is "attention" or "persons" ? null : await _context.ProctoringLogs.AsNoTracking().Where(l => l.AttemptId == active.AttemptId && l.SessionAttemptId == active.SessionAttemptId && l.IncidentKey == "absence")
            .OrderByDescending(l => l.LogId).Select(l => new { id = l.LogId, duration = l.ObservedDurationSeconds, startedAt = l.IncidentStartedAtUtc, endedAt = l.IncidentEndedAtUtc, recoveryStatus = l.RecoveryStatus }).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (feature == "absence")
            HttpContext.RequestServices.GetService<ILogger<ExamController>>()?.LogInformation(
                "Absence trace {RequestId} attempt {AttemptId} incident {IncidentId} state {PresenceState} verifiedSeconds {Seconds} signal {Signal} newLogs {LogIds} workerMs {WorkerMs} inferenceMs {InferenceMs} elapsedMs {ElapsedMs}",
                HttpContext.TraceIdentifier, active.AttemptId, absenceIncident?.id, presence?.State,
                presence?.ElapsedSeconds, result.Data.Signals.GetValueOrDefault("absence"),
                string.Join(",", logIds), workerMs, metrics?.FrameProcessingMs, (DateTime.Now - observationStartedAt).TotalMilliseconds);
        if (feature == "absence")
            return Json(new
            {
                ok = true, observationOnly = true, metrics, presenceStatus = presence,
                capabilities = result.Data.Capabilities, absenceIncident, evidenceLogIds, logIds,
                state = logIds.Count > 0 ? await _states.GetAsync(active.AttemptId, HttpContext.RequestAborted) : null
            });
        var peopleIncident = feature != "persons" ? null : await _context.ProctoringLogs.AsNoTracking()
            .Where(l => l.AttemptId == active.AttemptId && l.SessionAttemptId == active.SessionAttemptId && l.IncidentKey == "persons")
            .OrderByDescending(l => l.LogId).Select(l => new { id = l.LogId, endedAt = l.IncidentEndedAtUtc,
                confirmed = l.AlertType == ProctoringAlertType.MultipleFaces && l.CountsAsViolation })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        var behaviorLogs = await _context.ProctoringLogs.AsNoTracking().Where(l => l.AttemptId == active.AttemptId &&
            l.SessionAttemptId == active.SessionAttemptId && l.IncidentEndedAtUtc == null &&
            l.IncidentKey != null && l.IncidentKey != "mouth" && incidentKeys.Contains(l.IncidentKey))
            .OrderByDescending(l => l.LogId).Take(4).ToListAsync(HttpContext.RequestAborted);
        var behaviorWarnings = behaviorLogs.Select(l => new {
            id = l.LogId,
            type = l.AlertType.ToString(),
            title = TeacherAlertContract.Title(l.AlertType),
            countsAsViolation = l.CountsAsViolation,
            duration = l.ObservedDurationSeconds,
            count = l.MaxSimultaneousPeople
        }).ToArray();
        return Json(new
        {
            ok = true,
            observationOnly = true,
            metrics,
            behaviorWarnings,
            faceNotVisibleEvent = await _context.ProctoringLogs.AsNoTracking().AnyAsync(l => logIds.Contains(l.LogId) && l.AlertType == ProctoringAlertType.FaceNotVisible),
            peopleIncident,
            peopleSignal = feature == "persons" ? result.Data.Signals.GetValueOrDefault("persons") : null,
            attentionStatus = result.Data.AttentionStatus,
            capabilities = result.Data.Capabilities,
            presenceStatus = presence,
            absenceTrace = feature == "absence" ? new { requestId = HttpContext.TraceIdentifier,
                attemptId = active.AttemptId, receivedAt = observationStartedAt.ToUniversalTime(),
                completedAt = DateTime.UtcNow, elapsedSeconds = presence?.ElapsedSeconds } : null,
            absenceIncident,
            evidenceLogIds,
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
        var dataUrl = prefix + Convert.ToBase64String(image.Bytes);
        string? sharedKey = null;
        var logs = await _context.ProctoringLogs.Where(x => logIds.Contains(x.LogId) && x.StudentId == StudentId).ToListAsync();
        foreach (var log in logs)
        {
            if (log.IncidentKey == "absence")
            {
                log.ScreenshotPath ??= await _evidenceStore.SaveDataUrlAsync(StudentId, examId, attemptId,
                    dataUrl, HttpContext.RequestAborted, absenceIncidentId: log.LogId);
                continue;
            }
            var key = sharedKey ??= await _evidenceStore.SaveDataUrlAsync(StudentId, examId, attemptId,
                dataUrl, HttpContext.RequestAborted);
            if (log.IncidentKey == "persons" && log.ScreenshotPath is not null && key is not null)
                _context.ProctoringLogs.Add(new ProctoringLog
                {
                    ParentIncidentId = log.LogId,
                    StudentId = log.StudentId,
                    ExamId = log.ExamId,
                    AttemptId = log.AttemptId,
                    SessionAttemptId = log.SessionAttemptId,
                    AlertType = ProctoringAlertType.PersonEvidence,
                    ScreenshotPath = key,
                    CountsAsViolation = false,
                    PolicyProcessed = true,
                    ModelName = "maximum-count screenshot",
                    FrameCapturedAt = DateTime.UtcNow,
                    MaxSimultaneousPeople = log.MaxSimultaneousPeople,
                    CreatedDate = DateTime.UtcNow
                });
            log.ScreenshotPath ??= key;
        }
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

        if (result.Data.Classes.Count == 0 || result.Data.FrameWidth <= 0 || result.Data.FrameHeight <= 0)
            return StatusCode(503, new { ok = false, message = "Object monitoring capability metadata is unavailable. Restart the updated object service." });
        var logIds = new List<int>(); var pendingPolicy = new List<int>(); var activeObjects = new List<int>();
        string[] unsupportedClasses = [];
        var objectExamId = active.Attempt.ExamId;
        var objectSessionId = active.SessionAttemptId;
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            _context.ChangeTracker.Clear(); logIds.Clear(); pendingPolicy.Clear(); activeObjects.Clear(); unsupportedClasses = [];
            active = await _context.ExamSessionAttempts.Include(s => s.Attempt).SingleAsync(s => s.SessionAttemptId == objectSessionId, HttpContext.RequestAborted);
            await using (var tx = await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted))
            {
                await ProctoringStateService.LockAttemptAsync(_context, active.AttemptId, HttpContext.RequestAborted);
                await _context.Entry(active).ReloadAsync(HttpContext.RequestAborted);
                await _context.Entry(active.Attempt!).ReloadAsync(HttpContext.RequestAborted);
                var now = observationStartedAt.ToUniversalTime();
                if (active.Status != AttemptStatus.InProgress || active.Attempt!.Status != AttemptStatus.InProgress ||
                    active.Attempt.TeacherResumedAtUtc >= now)
                    return;

                var exam = await _context.Exams.AsNoTracking().SingleAsync(e => e.ExamId == objectExamId);
                var policy = ProctoringObjectPolicy.Read(exam.ObjectPolicyJson);
                unsupportedClasses = policy.Prohibited
                    .Where(label => !result.Data.Classes.Contains(label, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                ObjectSceneState tracking;
                try { tracking = System.Text.Json.JsonSerializer.Deserialize<ObjectSceneState>(active.ObjectTrackingJson ?? "{}") ?? new(); } catch (System.Text.Json.JsonException) { tracking = new(); }
                if (tracking.LastFrame >= now) return;
                var changed = ContextObjectTracker.Update(tracking, result.Data, policy, now);
                foreach (var track in tracking.Tracks)
                {
                    ProctoringLog? log = track.LogId is int id ? await _context.ProctoringLogs.SingleOrDefaultAsync(l => l.LogId == id && l.AttemptId == active.AttemptId) : null;
                    if (log is null && ContextObjectTracker.Ready(track, policy))
                    {
                        log = new ProctoringLog
                        {
                            StudentId = StudentId,
                            ExamId = objectExamId,
                            AttemptId = active.AttemptId,
                            SessionAttemptId = active.SessionAttemptId,
                            AlertType = track.ReviewOnly ? ProctoringAlertType.ObjectObservation : ProctoringAlertType.ElectronicDeviceDetected,
                            IncidentKey = "object:" + track.Id[..24], // database column is nvarchar(32)
                            ClientEventId = "object:" + active.SessionAttemptId + ":" + track.Id,
                            ModelName = ProctoringObjectPolicy.Title(track.Label),
                            ModelVersion = result.Data.ModuleVersion,
                            ConfidenceScore = track.MaxConfidence,
                            Severity = track.ReviewOnly ? ProctoringSeverity.Medium : ProctoringSeverity.High,
                            Description = track.ReviewOnly
                                ? $"{ProctoringObjectPolicy.Title(track.Label)} across multiple camera frames. Its role in the examination is uncertain."
                                : $"[OBJECT_{track.Label.ToUpperInvariant()}] {ProctoringObjectPolicy.Title(track.Label)} across multiple camera frames. This item is prohibited for this examination.",
                            CountsAsViolation = false,
                            PolicyPending = !track.ReviewOnly,
                            PolicyProcessed = track.ReviewOnly,
                            FrameCapturedAt = observationStartedAt,
                            CreatedDate = DateTime.Now,
                            IncidentStartedAtUtc = track.First,
                            RecoveryStatus = "Visible"
                        };
                        _context.ProctoringLogs.Add(log); await _context.SaveChangesAsync(); track.LogId = log.LogId; logIds.Add(log.LogId);
                    }
                    if (log is not null)
                    {
                        log.ConfidenceScore = Math.Max(log.ConfidenceScore ?? 0m, track.MaxConfidence);
                        log.ObservedDurationSeconds = Math.Max(log.ObservedDurationSeconds ?? 0, (track.Last - (log.IncidentStartedAtUtc ?? track.First)).TotalSeconds);
                        log.RecoveryStatus = track.Closed ? "NoLongerObserved" : track.Uncertain ? "TrackingUncertain" :
                            changed ? "SceneChanged" : track.MissingSince.HasValue ? "TemporarilyUnobserved" : "Visible";
                        if (track.Closed) log.IncidentEndedAtUtc = track.Last;
                        if (log.MachineObservationsJson is null) log.MachineObservationsJson = System.Text.Json.JsonSerializer.Serialize(new { label = track.Label, trackId = track.Id, physicalStatus = "unverified", interaction = "unavailable", context = track.Context, maximumConfidence = track.MaxConfidence, detectionSamples = track.Samples, sceneRevision = tracking.Revision, policyVersion = policy.Version, modelHash = result.Data.ModelSha256 });
                        if (!track.ReviewOnly && !track.Closed && !track.Dismissed && !track.Uncertain && !track.MissingSince.HasValue &&
                            tracking.LastUsableFrame == now) activeObjects.Add(log.LogId);
                        if (log.PolicyPending && !log.PolicyProcessed) pendingPolicy.Add(log.LogId);
                    }
                }
                active.ObjectTrackingJson = System.Text.Json.JsonSerializer.Serialize(tracking);
                await _context.SaveChangesAsync(); await tx.CommitAsync();
            }
        });
        foreach (var id in pendingPolicy) await _states.ApplyLogAsync(id, HttpContext.RequestAborted);
        if (!request.QueueEvidence)
            await AttachObservationImageAsync(logIds, image, objectExamId, active.AttemptId);
        foreach (var id in logIds.Except(pendingPolicy))
            await _states.NotifyObservationAsync(id, HttpContext.RequestAborted);
        var visibleObjectIds = logIds.Concat(activeObjects).Distinct().ToArray();
        var objectWarnings = await _context.ProctoringLogs.AsNoTracking().Where(l => visibleObjectIds.Contains(l.LogId) && l.StudentId == StudentId)
            .Select(l => new { id = l.LogId, name = l.ModelName, reviewOnly = !l.CountsAsViolation })
            .ToListAsync(HttpContext.RequestAborted);
        return Json(new
        {
            ok = true,
            ready = true,
            observationOnly = logIds.Count == 0,
            objectWarnings,
            detections = result.Data.Detections,
            unsupportedClasses,
            cameraUncertain = result.Data.FrameUsable == false,
            logIds,
            activeObjectIncidentIds = activeObjects,
            physicalVerificationAvailable = false,
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
        if (request.AudioMonotonicStart is double start && (!double.IsFinite(start) || start < 0) || request.AudioMonotonicEnd is double end && (!double.IsFinite(end) || end < 0) || request.AudioMonotonicStart > request.AudioMonotonicEnd) return BadRequest(new { message = "Invalid audio timing." });
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
            var recent = DateTime.Now.AddHours(-24);
            detectedLog = await _context.ProctoringLogs.FirstOrDefaultAsync(x => x.LogId == detectionId &&
                x.StudentId == StudentId && x.SessionAttemptId == active.SessionAttemptId && x.AttemptId == active.AttemptId &&
                x.ParentIncidentId == null && x.ModelName == "Speech screening" && x.CreatedDate >= recent);
        }
        var spokenQuestions = new List<Question>();
        // Delayed transcription must NEVER silently use the currently displayed
        // question. A missing/or mismatched screening anchor means context UNKNOWN.
        var firstSpeechUtc = detectedLog?.IncidentStartedAtUtc ?? detectedLog?.FrameCapturedAt?.ToUniversalTime();
        var lastSpeechUtc = detectedLog?.IncidentEndedAtUtc ?? firstSpeechUtc;
        var capturedUtc = request.AudioCapturedAtUtc?.UtcDateTime;
        var captureTimeAligned = detectedLog is not null && capturedUtc.HasValue &&
            (firstSpeechUtc.HasValue && lastSpeechUtc.HasValue && capturedUtc >= firstSpeechUtc.Value.AddSeconds(-5) &&
                capturedUtc <= lastSpeechUtc.Value.AddSeconds(5));
        // The original mouth observation only supports audio near the episode's
        // first screening sample. Later windows may contain a different speaker.
        var windowSpeakerSupported = detectedLog?.SpeakerAttribution == "mouth_correlated" &&
            capturedUtc is DateTime speechCapture && firstSpeechUtc is DateTime firstSpeech &&
            Math.Abs((speechCapture - firstSpeech).TotalSeconds) <= 2;
        var capturedId = request.QuestionId ?? detectedLog?.QuestionIdAtCapture;
        var captureQuestionAligned = capturedId.HasValue && request.QuestionIds.All(id => id == capturedId);
        var captureContextReliable = !detectionOnly && captureTimeAligned && captureQuestionAligned;
        var contextIds = detectionOnly
            ? request.QuestionIds.Append(request.QuestionId ?? 0).Where(id => id > 0).Distinct().ToArray()
            : captureContextReliable ? new[] { capturedId!.Value } : Array.Empty<int>();
        if (contextIds.Length > 8) return BadRequest(new { ok = false, message = "Too many question contexts." });
        foreach (var questionId in contextIds)
        {
            var ownedQuestion = detectionOnly ? await GetSessionQuestionAsync(active, questionId) : await CapturedSpeechQuestion(detectedLog, questionId);
            if (ownedQuestion is null) { if (detectionOnly) return BadRequest(new { ok = false, message = "Invalid question context." }); captureContextReliable = false; continue; }
            spokenQuestions.Add(ownedQuestion);
        }
        var analysisKey = detectedLog is not null ? $"asr:{detectedLog.LogId}:{capturedId ?? 0}:{clipKey}" : null;
        if (analysisKey is not null && await _context.ProctoringLogs.AnyAsync(l => l.ClientEventId == analysisKey, HttpContext.RequestAborted))
            return Json(new { ok = true, duplicate = true, detectionLogId = detectedLog!.LogId });
        var audio = new SpeechAudio(bytes, mediaType, request.Audio.FileName)
        {
            DetectionOnly = detectionOnly,
            Contexts = spokenQuestions.Select(q => new SpeechQuestionContext(q.QuestionId, q.QuestionText,
                new[] { q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.OptionE ?? "" }) {
                    ExamTitle = active.Attempt.Exam?.ExamTitle,
                    Subject = q.Subject?.SubjectName
                }).ToArray()
        };
        var result = streamedResult ?? await _speechRecognition.TranscribeAsync(audio, HttpContext.RequestAborted);
        var speechUnavailable = !result.Success || result.Data is null;
        IActionResult Unavailable() =>
            StatusCode(result.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                new { ok = false, ready = false, message = result.Message });
        if (speechUnavailable && detectedLog is null)
            return Unavailable();

        var observationNotifications = new HashSet<int>();
        var contextNotifications = new HashSet<int>();
        var speechPolicyIds = new HashSet<int>();
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var anchorId = detectedLog?.LogId;
            speechPolicyIds.Clear();
            observationNotifications.Clear();
            contextNotifications.Clear();
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            await ProctoringStateService.LockAttemptAsync(_context, request.AttemptId, HttpContext.RequestAborted);
            active = (await FindActiveSessionAsync(request.AttemptId, request.SessionAttemptId))!;
            if (active?.Attempt is null) return (IActionResult)BadRequest(new { message = "Session is no longer active." });
            detectedLog = anchorId.HasValue ? await _context.ProctoringLogs.SingleAsync(l => l.LogId == anchorId.Value) : null;
            if (analysisKey is not null && await _context.ProctoringLogs.AnyAsync(l => l.ClientEventId == analysisKey, HttpContext.RequestAborted))
                return Json(new { ok = true, duplicate = true, detectionLogId = anchorId });
            if (speechUnavailable && detectedLog is { SpeechTranscript: null })
                detectedLog.MachineObservationsJson = EnrichSpeechMetadata(detectedLog.MachineObservationsJson,
                    System.Text.Json.JsonSerializer.Serialize(new { transcriptionStatus = "unavailable", speaker = detectedLog.SpeakerAttribution, source = "microphone" }));
            var response = speechUnavailable ? Unavailable() : await PersistSpeechAsync(result.Data!);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
            // Publish only committed incidents; release the transaction before state reads.
            await transaction.DisposeAsync();
            foreach (var id in speechPolicyIds) await _states.ApplyLogAsync(id, HttpContext.RequestAborted);
            foreach (var id in observationNotifications) await _states.NotifyObservationAsync(id, HttpContext.RequestAborted);
            foreach (var id in contextNotifications) await _states.NotifySpeechContextAsync(id, HttpContext.RequestAborted);
            if (response is JsonResult json && speechPolicyIds.Count > 0)
            {
                var payload = System.Text.Json.JsonSerializer.SerializeToNode(json.Value)!.AsObject();
                payload["state"] = System.Text.Json.JsonSerializer.SerializeToNode(
                    await _states.GetAsync(request.AttemptId, HttpContext.RequestAborted),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                return Json(payload);
            }
            return response;
        });

        async Task<IActionResult> PersistSpeechAsync(SpeechRecognitionResponse speechData)
        {
        var transcript = string.Join(' ', speechData.Transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var vadSpeech = double.IsFinite(speechData.VadSpeechSeconds) && speechData.VadSpeechSeconds >= .3;
        if (detectionOnly && !vadSpeech)
            speechData.SpeechDetected = false;
        if (!speechData.SpeechDetected || (!vadSpeech && (string.IsNullOrWhiteSpace(transcript) ||
            speechData.Confidence < _speechOptions.MinimumTranscriptConfidence)))
        {
            if (!detectionOnly && detectedLog is { SpeechTranscript: null })
            {
                detectedLog.MachineObservationsJson = EnrichSpeechMetadata(detectedLog.MachineObservationsJson, System.Text.Json.JsonSerializer.Serialize(new { transcriptionStatus = "no_intelligible_speech", speaker = "unknown", language = speechData.Language }));
                await _context.SaveChangesAsync(HttpContext.RequestAborted);
            }
            var elevatedNoise = false;
            var newNoiseEpisode = false;
            int? noiseLogId = null;
            if (detectionOnly)
            {
                var last = await _context.ProctoringLogs.Where(l => l.SessionAttemptId == active.SessionAttemptId && l.ModelName == "Speech screening")
                    .OrderByDescending(l => l.LogId).FirstOrDefaultAsync();
                if (last is not null) ObserveSpeechQuiet(last, observationStartedAt.ToUniversalTime());
                // Energy is used only for a non-counting environmental sound advisory.
                var rms = SpeechNoisePolicy.Pcm16WavRms(bytes);
                if (SpeechNoisePolicy.ElevatedUnknownNoise(rms, speechData.SpeechDetected))
                {
                    var previousNoise = await _context.ProctoringLogs.Where(x =>
                        x.AttemptId == active.AttemptId && x.SessionAttemptId == active.SessionAttemptId &&
                        x.ModelName == "Ambient audio screening").OrderByDescending(x => x.LogId).FirstOrDefaultAsync(HttpContext.RequestAborted);
                    elevatedNoise = true;
                    if (previousNoise?.IncidentEndedAtUtc is DateTime lastNoise &&
                        observationStartedAt.ToUniversalTime() - lastNoise < TimeSpan.FromSeconds(3))
                    {
                        previousNoise.IncidentEndedAtUtc = observationStartedAt.ToUniversalTime();
                        previousNoise.ObservedDurationSeconds = Math.Max(0, (observationStartedAt.ToUniversalTime() - previousNoise.IncidentStartedAtUtc!.Value).TotalSeconds);
                        noiseLogId = previousNoise.LogId;
                    }
                    else
                    {
                        var environmental = new ProctoringLog
                        {
                            StudentId = StudentId,
                            ExamId = active.Attempt.ExamId,
                            AttemptId = active.AttemptId,
                            SessionAttemptId = active.SessionAttemptId,
                            AlertType = ProctoringAlertType.AudioDetected,
                            ModelName = "Ambient audio screening",
                            IncidentKey = "ambient",
                            IncidentStartedAtUtc = observationStartedAt.ToUniversalTime(),
                            IncidentEndedAtUtc = observationStartedAt.ToUniversalTime(),
                            RecoveryStatus = "Observed",
                            Description = "Elevated environmental sound; its source is unknown. Teacher review requested. No cheating strike.",
                            CountsAsViolation = false,
                            PolicyPending = false,
                            PolicyProcessed = true,
                            FrameCapturedAt = observationStartedAt,
                            CreatedDate = DateTime.Now,
                            ClientEventId = Guid.NewGuid().ToString("N"),
                            IsReviewed = false,
                            MachineObservationsJson = System.Text.Json.JsonSerializer.Serialize(new { audioRms = rms, vadSpeechDetected = false, classification = "environmental_sound_uncertain" })
                        };
                        _context.ProctoringLogs.Add(environmental);
                        await _context.SaveChangesAsync(HttpContext.RequestAborted);
                        observationNotifications.Add(environmental.LogId);
                        noiseLogId = environmental.LogId;
                        newNoiseEpisode = true;
                        elevatedNoise = true;
                    }
                }
            }
            return Json(new
            {
                ok = true,
                ready = true,
                speechDetected = false,
                speechKind = elevatedNoise ? "environmental_noise_uncertain" : "none",
                notifyStudent = newNoiseEpisode,
                detectionLogId = noiseLogId,
                observationOnly = true,
                language = speechData.Language,
                confidence = speechData.Confidence,
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
            var previousScreening = await _context.ProctoringLogs
                .Where(x => x.SessionAttemptId == active.SessionAttemptId && x.ModelName == "Speech screening")
                .OrderByDescending(x => x.LogId).FirstOrDefaultAsync();
            var recoveredSpeech = incidentState.GetValueOrDefault("speech");
            var previousLastSpeech = previousScreening?.IncidentEndedAtUtc ?? previousScreening?.FrameCapturedAt?.ToUniversalTime() ?? previousScreening?.CreatedDate.ToUniversalTime();
            var continuousConversation = previousScreening is not null && previousScreening.RecoveryStatus != "Ended" && previousLastSpeech.HasValue &&
                observationStartedAt.ToUniversalTime() >= previousScreening.IncidentStartedAtUtc &&
                !(recoveredSpeech?.ClearSinceUtc is DateTime clear && !recoveredSpeech.Emitted && clear >= previousLastSpeech.Value);
            var conversation = continuousConversation ? previousScreening!.ModelVersion : Guid.NewGuid().ToString("N");
            // One durable observation per continuous conversation, not one per
            // 500ms VAD packet. Reusing its ID prevents repeated teacher alerts.
            if (continuousConversation && previousScreening is not null)
            {
                // A transport gap is not proof of silence. Only fresh quiet VAD
                // observations close the canonical conversation.
                var quiet = SpeechMetadata(previousScreening.MachineObservationsJson);
                quiet.Remove("quietSinceUtc"); quiet.Remove("lastQuietUtc");
                previousScreening.MachineObservationsJson = quiet.ToJsonString();
                await CaptureSpeechQuestions(previousScreening, spokenQuestions);
                // Only refresh the durable episode heartbeat every ~2s, not on
                // every 500ms packet. Long conversations remain one observation.
                if (!previousLastSpeech.HasValue || (observationStartedAt.ToUniversalTime() - previousLastSpeech.Value).TotalSeconds >= 2)
                {
                    previousScreening.IncidentEndedAtUtc = observationStartedAt.ToUniversalTime();
                    previousScreening.ObservedDurationSeconds = Math.Max(0, (observationStartedAt.ToUniversalTime() -
                        (previousScreening.IncidentStartedAtUtc ?? previousScreening.CreatedDate.ToUniversalTime())).TotalSeconds);
                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                }
                return Json(new
                {
                    ok = true,
                    speechDetected = true,
                    needsContext = true,
                    speechKind = mouthSupported ? "candidate_speech_supported" : "speech_speaker_unknown",
                    notifyStudent = false,
                    detectionLogId = previousScreening.LogId,
                    logIds = Array.Empty<int>()
                });
            }
            var screening = new ProctoringLog
            {
                StudentId = StudentId,
                ExamId = active.Attempt.ExamId,
                AttemptId = active.AttemptId,
                SessionAttemptId = active.SessionAttemptId,
                AlertType = ProctoringAlertType.AudioDetected,
                ModelName = "Speech screening",
                IncidentKey = "speech",
                IncidentStartedAtUtc = observationStartedAt.ToUniversalTime(),
                RecoveryStatus = "Active",
                ModelVersion = conversation,
                Description = "Speech was heard near the microphone. Review the audio and available camera context.",
                // Do not substitute a later/current question when the audio packet omitted it.
                QuestionIdAtCapture = request.QuestionId,
                SpeakerAttribution = mouthSupported ? "mouth_correlated" : "unknown",
                DetectedFaceCount = null,
                CountsAsViolation = false,
                PolicyProcessed = true,
                FrameCapturedAt = observationStartedAt,
                IncidentEndedAtUtc = observationStartedAt.ToUniversalTime(),
                ClientEventId = clipKey + Guid.NewGuid().ToString("N"),
                CreatedDate = DateTime.Now
            };
            _context.ProctoringLogs.Add(screening);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            screening.MachineObservationsJson = System.Text.Json.JsonSerializer.Serialize(new { transcriptionStatus = "pending", speaker = screening.SpeakerAttribution, source = "microphone", enhanced = false });
            await CaptureSpeechQuestions(screening, spokenQuestions);
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            // SignalR + Teacher roster use this same durable log. It is review-only:
            // no strike is issued until an independently qualifying incident exists.
            observationNotifications.Add(screening.LogId);
            return Json(new
            {
                ok = true,
                speechDetected = true,
                needsContext = true,
                speechKind = mouthSupported ? "candidate_speech_supported" : "speech_speaker_unknown",
                notifyStudent = true,
                logIds = Array.Empty<int>(),
                detectionLogId = screening.LogId,
                reviewOnly = true
            });
        }

        var semantic = speechData.ContextAnalysis;
        var reliableSpeech = SpeechEvidencePolicy.Intelligible(speechData, _speechOptions.MinimumTranscriptConfidence);
        var spokenQuestion = spokenQuestions.FirstOrDefault(q => q.QuestionId == semantic?.QuestionId);
        var localMatches = spokenQuestions.Select(q => new { Question = q, Match = SpeechContextAnalyzer.Analyze(
            transcript, q.QuestionText, new[] { q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.OptionE }) }).ToList();
        var selectedLocal = localMatches.FirstOrDefault(m => m.Question.QuestionId == semantic?.QuestionId &&
            (m.Match.Related || m.Match.MatchKind == "question_term")) ??
            localMatches.FirstOrDefault(m => m.Match.Related) ?? localMatches.FirstOrDefault(m => m.Match.Ambiguous);
        var localMatch = selectedLocal?.Match ?? SpeechContextAnalyzer.Analyze(transcript, "", Array.Empty<string>());
        var semanticMatchedCaptured = captureContextReliable &&
            spokenQuestion?.QuestionId == capturedId &&
            SpeechContextAnalyzer.IsHighSeverity(semantic);
        var semanticParaphrase = semanticMatchedCaptured && SpeechContextAnalyzer.SupportedParaphrase(semantic, transcript);
        var contextReview = captureContextReliable && reliableSpeech &&
            (localMatch.Related || semanticMatchedCaptured && !localMatch.Ambiguous || semanticParaphrase);
        var highSeveritySpeech = contextReview && (!localMatch.Ambiguous || semanticParaphrase) &&
            (localMatch.Related || semanticMatchedCaptured);
        var candidateSpeech = reliableSpeech && _speechOptions.TalkingProhibited && windowSpeakerSupported;
        var unrelatedSpeechConfirmed = captureContextReliable && reliableSpeech &&
            !contextReview && !localMatch.Related && !localMatch.Ambiguous &&
            SpeechContextAnalyzer.ConfirmedUnrelated(semantic);
        // Question-specific content may come from any audible speaker. Keep speaker
        // attribution independent from content: generic cues and semantic similarity
        // alone still cannot trigger a strike. Require a captured question, usable
        // transcript, Model 5 agreement, and an independent phrase or concept anchor.
        var questionTermCorroborated = spokenQuestion is not null &&
            SpeechContextAnalyzer.HasSpecificQuestionAnchor(transcript, spokenQuestion.QuestionText,
                new[] { spokenQuestion.OptionA, spokenQuestion.OptionB, spokenQuestion.OptionC,
                    spokenQuestion.OptionD, spokenQuestion.OptionE });
        var questionSpeechConfirmed = highSeveritySpeech && semanticMatchedCaptured &&
            (localMatch.Related || questionTermCorroborated || semanticParaphrase);
        var contextMatch = new SpeechContextMatch(contextReview,
            localMatch.Related || localMatch.Ambiguous ? localMatch.Reason :
            "Semantic comparison suggests question relevance; teacher review required.",
            localMatch.MatchedPhrases ?? semantic?.MatchedPhrases, localMatch.MatchKind, localMatch.Ambiguous);
        var matchedQuestionId = contextReview ?
            (selectedLocal?.Question.QuestionId ?? spokenQuestion?.QuestionId ?? detectedLog?.QuestionIdAtCapture) : null;
        int? speechNotice = null;
        if (detectedLog?.ModelName == "Speech screening" && analysisKey is not null)
        {
            // Enrich the SAME durable VAD episode rather than creating a separate
            // unlinked notification. Voice alone cannot prove speaker identity.
            var unrelatedOrdinal = 0;
            if (unrelatedSpeechConfirmed && _speechOptions.TalkingProhibited && !_speechOptions.ObservationOnly)
            {
                var decisionKey = $"speech-policy:{detectedLog.LogId}";
                var existingDecision = await _context.ProctoringLogs.SingleOrDefaultAsync(l => l.ClientEventId == decisionKey);
                if (existingDecision is not null)
                    int.TryParse(existingDecision.ModelVersion, out unrelatedOrdinal);
                else if (!detectedLog.CountsAsViolation && detectedLog.AlertType != ProctoringAlertType.AnswerPromptSpeech)
                {
                    unrelatedOrdinal = 1 + await _context.ProctoringLogs.CountAsync(l =>
                        l.AttemptId == active.AttemptId && l.ModelName == "Speech policy decision");
                    _context.ProctoringLogs.Add(new ProctoringLog {
                        ParentIncidentId = detectedLog.LogId, StudentId = StudentId, ExamId = active.Attempt.ExamId,
                        AttemptId = active.AttemptId, SessionAttemptId = active.SessionAttemptId,
                        ClientEventId = decisionKey, ModelName = "Speech policy decision",
                        ModelVersion = unrelatedOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        AlertType = ProctoringAlertType.Other, CountsAsViolation = false, PolicyProcessed = true,
                        CreatedDate = DateTime.UtcNow, Description = "Confirmed unrelated speech episode; attempt policy decision."
                    });
                    if (unrelatedOrdinal <= 2)
                    {
                        active.Attempt.StateVersion++;
                        speechNotice = unrelatedOrdinal;
                        contextNotifications.Add(detectedLog.LogId);
                    }
                }
            }
            var previousContext = detectedLog.AlertType == ProctoringAlertType.AnswerPromptSpeech;
            var rootMetadata = SpeechMetadata(detectedLog.MachineObservationsJson);
            var previousObservations = rootMetadata["sourceObservations"] is System.Text.Json.Nodes.JsonValue source &&
                source.TryGetValue<string>(out var sourceJson) ? SpeechMetadata(sourceJson) : rootMetadata;
            var previousConfirmed = previousObservations["questionSpeechConfirmed"] is System.Text.Json.Nodes.JsonValue confirmed &&
                confirmed.TryGetValue<bool>(out var confirmedValue) && confirmedValue;
            var updateContext = contextMatch.Related && (!previousContext || questionSpeechConfirmed && !previousConfirmed);
            if (updateContext)
            {
                detectedLog.AlertType = ProctoringAlertType.AnswerPromptSpeech;
                detectedLog.Severity = highSeveritySpeech ? ProctoringSeverity.High : ProctoringSeverity.Medium;
                detectedLog.Description = $"Possible discussion of exam question #{matchedQuestionId}. Teacher review required; speaker is not confirmed.";
                detectedLog.SemanticSimilarity = semantic is not null && semantic.QuestionId == matchedQuestionId &&
                    semantic.Score is double contextScore && double.IsFinite(contextScore) && contextScore is >= -1 and <= 1
                    ? (decimal)contextScore : null;
                detectedLog.MatchedQuestionId = matchedQuestionId;
            }
            else if (unrelatedSpeechConfirmed && unrelatedOrdinal > 0 && detectedLog.AlertType != ProctoringAlertType.AnswerPromptSpeech)
            {
                detectedLog.AlertType = ProctoringAlertType.TalkingDetected;
                detectedLog.Description = unrelatedOrdinal == 1 ? "First Speech Notice — please remain silent. Speech notice 1 of 2." :
                    unrelatedOrdinal == 2 ? "Final Speech Notice — further talking may result in a cheating warning." :
                    "Repeated intelligible unrelated speech detected. Speech warning policy applies.";
            }
            var windowTranscript = transcript[..Math.Min(transcript.Length, 4000)];
            var windowMetadata = EnrichSpeechMetadata(detectedLog.MachineObservationsJson, System.Text.Json.JsonSerializer.Serialize(new {
                transcriptionStatus = string.IsNullOrWhiteSpace(transcript) ? "unavailable" : reliableSpeech ? "available" : "uncertain",
                language = speechData.Language, speaker = windowSpeakerSupported ? "mouth_correlated" : "unknown", model = speechData.ModuleVersion,
                clipSeconds = speechData.DurationSeconds, capturedAt = request.AudioCapturedAtUtc,
                monotonicStart = request.AudioMonotonicStart, monotonicEnd = request.AudioMonotonicEnd,
                timingSource = "client_capture_clock_untrusted", serverReceivedAt = observationStartedAt.ToUniversalTime(),
                originalQuestionId = capturedId, matchedQuestionId,
                capturedQuestions = spokenQuestions.Select(q => new { q.QuestionId, q.QuestionText, q.OptionA, q.OptionB, q.OptionC, q.OptionD, q.OptionE }),
                captureContextReliable, matchKind = localMatch.MatchKind, matchAmbiguous = localMatch.Ambiguous,
                contextStatus = !captureContextReliable ? "missing_capture_context" : !reliableSpeech ? "unintelligible" :
                    contextMatch.Related ? semantic?.Ready == true ? "possible_match" : "phrase_match_semantics_unavailable" :
                    contextMatch.Ambiguous ? "generic_cue" : semantic?.Ready == true ? "no_supported_match" : "semantic_unavailable",
                candidateSpeechConfirmed = candidateSpeech, questionSpeechConfirmed, questionTermCorroborated, semanticParaphrase,
                unrelatedSpeechConfirmed, unrelatedSpeechEpisode = unrelatedOrdinal,
                speechNoticeNumber = unrelatedOrdinal is 1 or 2 ? unrelatedOrdinal : (int?)null,
                speechClassification = questionSpeechConfirmed ? "exam_related" : unrelatedSpeechConfirmed ? "unrelated" : "uncertain",
                matchedPhrases = contextMatch.MatchedPhrases ?? Array.Empty<string>(),
                semanticScore = semantic?.Score, semanticMargin = semantic?.RelevanceMargin,
                segments = speechData.Segments.Select(s => new { s.Start, s.End }), source = "microphone", enhanced = false
            }));
            var awaitingPolicy = detectedLog.PolicyPending && !detectedLog.PolicyProcessed;
            if (detectedLog.SpeechTranscript is null || questionSpeechConfirmed && updateContext ||
                !awaitingPolicy && (updateContext || unrelatedSpeechConfirmed && detectedLog.AlertType == ProctoringAlertType.TalkingDetected))
            {
                detectedLog.SpeechTranscript = windowTranscript;
                if (rootMetadata.ContainsKey("sourceObservations"))
                {
                    rootMetadata["sourceObservations"] = windowMetadata;
                    detectedLog.MachineObservationsJson = rootMetadata.ToJsonString();
                }
                else
                    detectedLog.MachineObservationsJson = windowMetadata;
            }
            if (!_speechOptions.ObservationOnly && detectedLog.PolicyAppliedAt is null && !detectedLog.PolicyPending &&
                BlockingPolicy.ConfirmedSpeech(active.Attempt.Exam!, detectedLog))
            {
                detectedLog.PolicyPending = true;
                detectedLog.PolicyProcessed = false;
                speechPolicyIds.Add(detectedLog.LogId);
            }
            _context.ProctoringLogs.Add(new ProctoringLog {
                ParentIncidentId = detectedLog.LogId, StudentId = StudentId, ExamId = active.Attempt.ExamId,
                AttemptId = active.AttemptId, SessionAttemptId = active.SessionAttemptId,
                ClientEventId = analysisKey, AlertType = ProctoringAlertType.Other, ModelName = "Speech analysis",
                SpeechTranscript = windowTranscript, MachineObservationsJson = windowMetadata,
                QuestionIdAtCapture = captureContextReliable ? capturedId : null, MatchedQuestionId = matchedQuestionId,
                SpeakerAttribution = windowSpeakerSupported ? "mouth_correlated" : "unknown",
                FrameCapturedAt = capturedUtc,
                CountsAsViolation = false, PolicyProcessed = true,
                Description = "Transcript and capture-time analysis for this speech window.",
                CreatedDate = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            if (updateContext && !previousContext && !speechPolicyIds.Contains(detectedLog.LogId))
                contextNotifications.Add(detectedLog.LogId);
        }
        return Json(new
        {
            ok = true, ready = true, speechDetected = true,
            questionRelated = contextMatch.Related, questionId = matchedQuestionId,
            contextAlert = contextNotifications.Count > 0 || contextMatch.Related && speechPolicyIds.Count > 0,
            reviewOnly = speechPolicyIds.Count == 0, observationOnly = true,
            speechEvidenceId = candidateSpeech || questionSpeechConfirmed ? detectedLog?.LogId : null,
            semanticReady = semantic?.Ready == true, language = speechData.Language,
            speechNoticeNumber = speechNotice, speechNoticeId = speechNotice.HasValue ? detectedLog?.LogId : null,
            detectionLogId = detectedLog?.LogId, logIds = Array.Empty<int>()
        });
        }
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
        string eventId, IFormFile? recording, bool includesAudio, string? metadata = null)
    {
        if (StudentId is null) return Unauthorized();
        var segment = SessionRecordingMetadata.Read(metadata);
        if (!_proctoringOptions.CaptureEvidence || !Guid.TryParseExact(eventId, "N", out _) ||
            metadata is not null && segment is null ||
            (segment?.HasMedia ?? true) != (recording is not null))
            return BadRequest(new { ok = false });
        // Captured media may arrive after a technical hold; ownership still applies.
        var active = await _context.ExamSessionAttempts.Include(s => s.Attempt)
            .SingleOrDefaultAsync(s => s.SessionAttemptId == sessionAttemptId && s.AttemptId == attemptId && s.Attempt!.StudentId == StudentId);
        if (active is not null && active.StartedAt < DateTime.Now.AddDays(-1)) return StatusCode(410, new { ok = false });
        if (active?.Attempt is null) return BadRequest(new { ok = false });
        if (segment is not null && (DateTimeOffset.FromUnixTimeMilliseconds(segment.StartedAt).UtcDateTime <
            active.Attempt.StartedAt.ToUniversalTime().AddMinutes(-2) ||
            active.CompletedAt is DateTime ended &&
            DateTimeOffset.FromUnixTimeMilliseconds(segment.EndedAt).UtcDateTime > ended.ToUniversalTime().AddMinutes(2)))
            return BadRequest(new { ok = false });
        var key = segment is null ? eventId : $"rec:{sessionAttemptId}:{segment.TimelineId:N}:{segment.Source}:{segment.Sequence}";
        string? path = null;
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () => {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            await ProctoringStateService.LockEvidenceUploadsAsync(_context, attemptId, HttpContext.RequestAborted);
            var previous = await _context.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(x => x.ClientEventId == key);
            if (previous is not null)
                return previous.StudentId == StudentId && previous.AttemptId == attemptId && previous.SessionAttemptId == sessionAttemptId &&
                    (segment is null || previous.MachineObservationsJson == segment.Serialize())
                    ? Json(new { ok = true, acknowledged = true, duplicate = true }) : Conflict();
            var cutoff = DateTime.Now.AddMinutes(-1);
            var recent = await _context.ProctoringLogs.CountAsync(x => x.SessionAttemptId == sessionAttemptId &&
                x.AlertType == ProctoringAlertType.SessionRecording && x.CreatedDate >= cutoff);
            if (recent >= (segment is null ? 12 : 30)) return StatusCode(429, new { ok = false, message = "Recording uploads are arriving too quickly." });
            if (recording is not null)
            {
                path ??= await _evidenceStore.SaveUploadAsync(StudentId, active.Attempt.ExamId,
                    attemptId, recording, HttpContext.RequestAborted);
                if (path is null) return BadRequest(new { ok = false, message = "Recording clip was invalid or could not be stored." });
            }
            _context.ProctoringLogs.Add(new ProctoringLog
            {
                StudentId = StudentId,
                ExamId = active.Attempt.ExamId,
                AttemptId = attemptId,
                SessionAttemptId = sessionAttemptId,
                AlertType = ProctoringAlertType.SessionRecording,
                Severity = ProctoringSeverity.Low,
                RecordingPath = path,
                RecordingIncludesAudio = path is not null && (segment?.IncludesMicrophone ?? includesAudio),
                Description = segment is null ? "Routine camera/microphone session segment." : $"{segment.Source} timeline segment ({segment.Status}).",
                ModelName = segment is null ? "Session recorder" : SessionRecordingMetadata.ModelName,
                ModelVersion = segment is null ? "1" : "2",
                ClientEventId = key,
                FrameCapturedAt = segment is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(segment.StartedAt).UtcDateTime,
                MachineObservationsJson = segment?.Serialize(),
                PolicyProcessed = true,
                CountsAsViolation = false,
                IsReviewed = true,
                CreatedDate = DateTime.Now
            });
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
            return Json(new { ok = true, acknowledged = true });
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(22_000_000)]
    [Route("/Student/Exam/AttachProctoringEvidence")]
    public async Task<IActionResult> AttachProctoringEvidence(
        [FromForm] ProctoringEvidenceUploadRequest request,
        CancellationToken cancellationToken, int? attemptId = null, int? sessionAttemptId = null)
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
            ProctoringAlertType.FaceNotVisible,
            ProctoringAlertType.MultipleFaces,
            ProctoringAlertType.AdditionalPersonObservation,
            ProctoringAlertType.ObjectObservation,
            ProctoringAlertType.LookAway, ProctoringAlertType.LookingAwaySpeaking,
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
                ((attemptId != null && sessionAttemptId != null && x.AttemptId == attemptId && x.SessionAttemptId == sessionAttemptId) || (attemptId == null && sessionAttemptId == null && x.CreatedDate >= recentCutoff)),
                cancellationToken);

        if (log is null || !allowedEvidenceTypes.Contains(log.AlertType))
            return NotFound();
        if (log.IncidentKey == "absence:escalation")
            return BadRequest(new { ok = false, message = "Attach the absence clip to the original incident." });

        // Client retries are idempotent: keep the first valid event clip rather
        // than replacing it with a later retry segment.
        if (!string.IsNullOrWhiteSpace(log.VideoEvidencePath))
            return Json(new { acknowledged = true, ok = true, duplicate = true, logId = log.LogId });

        string? captureMetadata = log.MachineObservationsJson;
        if (log.IncidentKey is "absence" or "persons" || ProctoringStateService.IsCritical(log)) {
            try { captureMetadata = AbsenceEvidenceMetadata.Merge(captureMetadata, request.Metadata); }
            catch (ArgumentException) { return BadRequest(new { ok = false, message = "Invalid evidence capture metadata." }); }
        }
        var evidenceKey = await _evidenceStore.SaveUploadAsync(
            log.Student?.CandidateNumber ?? log.StudentId,
            log.ExamId ?? 0,
            log.AttemptId!.Value,
            request.EvidenceFile,
            cancellationToken, absenceIncidentId: log.IncidentKey == "absence" ? log.LogId : null);
        if (string.IsNullOrWhiteSpace(evidenceKey))
        {
            return BadRequest(new
            {
                ok = false,
                message = "The clip was rejected. Only a valid, size-limited WebM event clip is accepted."
            });
        }

        if (log.ScreenshotPath is null && !string.IsNullOrWhiteSpace(request.ScreenshotDataUrl))
            log.ScreenshotPath ??= await _evidenceStore.SaveDataUrlAsync(log.Student?.CandidateNumber ?? log.StudentId, log.ExamId ?? 0, log.AttemptId.Value, request.ScreenshotDataUrl, cancellationToken, absenceIncidentId: log.IncidentKey == "absence" ? log.LogId : null);
        if (_context.Database.IsRelational())
        {
            // Claim the incident's original exactly once across tabs and retries.
            await _context.ProctoringLogs
                .Where(x => x.LogId == log.LogId && x.StudentId == StudentId &&
                    (x.VideoEvidencePath == null || x.VideoEvidencePath.Trim() == ""))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.VideoEvidencePath, evidenceKey)
                    .SetProperty(x => x.RecordingIncludesAudio, request.IncludesAudio)
                    .SetProperty(x => x.MachineObservationsJson, captureMetadata)
                    .SetProperty(x => x.ScreenshotPath, x => x.ScreenshotPath ?? log.ScreenshotPath), cancellationToken);
            // A retry after an ambiguous commit may find its own key already saved.
            var persisted = await _context.ProctoringLogs.AsNoTracking()
                .Where(x => x.LogId == log.LogId && x.StudentId == StudentId)
                .Select(x => new { x.VideoEvidencePath, x.ScreenshotPath }).SingleAsync(cancellationToken);
            if (persisted.VideoEvidencePath != evidenceKey)
                await _evidenceStore.DeleteAsync(evidenceKey, cancellationToken);
            if (log.ScreenshotPath != null && persisted.ScreenshotPath != log.ScreenshotPath)
                await _evidenceStore.DeleteAsync(log.ScreenshotPath, cancellationToken);
            return Json(new { acknowledged = true, ok = true, logId = log.LogId });
        }
        var oldEvidenceKey = log.VideoEvidencePath;
        log.VideoEvidencePath = evidenceKey;
        log.RecordingIncludesAudio = request.IncludesAudio;
        log.MachineObservationsJson = captureMetadata;
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

        return Json(new { acknowledged = true, ok = true, logId = log.LogId });
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

        // A reviewed block may remain ProctoringBlocked for audit integrity,
        // but its released academic marks must still be visible to this student.
        if (attempt.ResultReleasedAtUtc.HasValue)
            return RedirectToAction(nameof(AttemptResult), new { attemptId });

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

        // Use the configured session rest. Clamp any
        // administrative value to the supported real-world maximum.
        var maximumRestMinutes = Math.Clamp(previous.Session.RestAfterMinutes, 0, 10);

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
            var restMinutes = Math.Clamp(previous.Session.RestAfterMinutes, 0, 10);
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
        var studentId = StudentId;
        var result = await AiExamProctoring.Services.Results.StudentResultAccess
            .ReleasedFor(_context, studentId)
            .Include(x => x.Attempt).ThenInclude(x => x!.Exam)
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.Attempt!.StudentId == studentId);
        if (result?.Attempt == null)
        {
            var pending = await _context.ExamAttempts.AsNoTracking()
                .Where(a => a.AttemptId == attemptId && a.StudentId == studentId &&
                    (a.Status == AttemptStatus.Completed || a.Status == AttemptStatus.AutoSubmitted ||
                     a.Status == AttemptStatus.ProctoringBlocked || a.Status == AttemptStatus.Disqualified))
                .Select(a => new { a.Status, a.SubmittedForViolations, a.IsDisqualified,
                    ExamTitle = a.Exam!.ExamTitle }).FirstOrDefaultAsync();
            if (pending is null) return NotFound();
            ViewBag.WaitingForTeacherReview = pending.Status == AttemptStatus.ProctoringBlocked ||
                pending.SubmittedForViolations || pending.IsDisqualified ||
                await Services.Results.ExamResultFinalizationService.PendingIntegrity(_context, attemptId).AnyAsync() ||
                await _context.ExamResults.AnyAsync(r => r.AttemptId == attemptId);
            ViewBag.PendingExamName = pending.ExamTitle;
            return View("ResultPending");
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

        // Only randomize the paper when the Teacher enabled ShuffleQuestions.
        // The seed makes one candidate's order stable across reload and resume.
        if (sa.Attempt?.Exam?.ShuffleQuestions == true)
        {
            var seedMaterial = $"candidate-order-v2|{sa.AttemptId}|{sa.SessionId}|{sa.SessionAttemptId}";
            var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(seedMaterial));
            var rng = new Random(BitConverter.ToInt32(seedBytes, 0));
            for (var index = rows.Count - 1; index > 0; index--)
            {
                var swapIndex = rng.Next(index + 1);
                (rows[index], rows[swapIndex]) = (rows[swapIndex], rows[index]);
            }
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

    private static async Task<List<FaceIdentityImage>> ReadFaceFramesAsync(IReadOnlyCollection<IFormFile> frames, CancellationToken cancellationToken, int minimumBytes = 8_000)
    {
        var images = new List<FaceIdentityImage>(frames.Count);
        foreach (var frame in frames)
        {
            if (frame.Length < minimumBytes || frame.Length > 5 * 1024 * 1024) continue;
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

    private async Task FinalizeAttempt(ExamAttempt attempt, bool callerHoldsAttemptLock = false)
    {
        // Only SubmitSessionCore holds its own lock and transaction. Continue,
        // Break and StartNextSession must use the transactional entry point.
        if (callerHoldsAttemptLock)
            await AiExamProctoring.Services.Results.ExamResultFinalizationService
                .FinalizeCompletedInTransactionAsync(_context, attempt.AttemptId,
                    DateTime.Now, HttpContext.RequestAborted);
        else
            await AiExamProctoring.Services.Results.ExamResultFinalizationService
                .FinalizeCompletedAsync(_context, attempt.AttemptId,
                    DateTime.Now, HttpContext.RequestAborted);
    }
}
