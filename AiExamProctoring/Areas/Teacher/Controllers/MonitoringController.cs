using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExamEntity = AiExamProctoring.Models.Exam;

namespace AiExamProctoring.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Roles = "Teacher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MonitoringController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IProctoringStateService _states;
    private readonly IProctoringEvidenceStore _evidence;

    public MonitoringController(ApplicationDbContext db, IProctoringEvidenceStore evidence, IProctoringStateService states)
    {
        _db = db;
        _states = states;
        _evidence = evidence;
    }

    private string? TeacherId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Limit the live room to current monitoring, not historical examinations.
    // Historical blocked/flagged candidates remain accessible in the evidence review area.
    private IQueryable<ExamEntity> AuthorizedLiveExams(string teacherId, DateTime localNow)
    {
        var identityReviewUntil = localNow.AddMinutes(-2);
        return _db.Exams.AsNoTracking().Where(e => e.IsPublished &&
            e.Status != ExamStatus.Completed && e.Status != ExamStatus.Cancelled &&
            (e.CreatedBy == teacherId || _db.ExamInvigilators.Any(i =>
                i.ExamId == e.ExamId && i.TeacherId == teacherId &&
                i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null && i.RevokedAtUtc == null)) &&
            (_db.ExamAttempts.Any(a => a.ExamId == e.ExamId && a.Status == AttemptStatus.InProgress &&
                a.SessionAttempts.Any(s => s.Status == AttemptStatus.InProgress)) ||
             _db.ExamAttempts.Any(a => a.ExamId == e.ExamId && a.Status == AttemptStatus.ProctoringBlocked &&
                 _db.ProctoringLogs.Any(l => l.AttemptId == a.AttemptId && l.AlertType == ProctoringAlertType.IdentityMismatch &&
                     l.Severity == ProctoringSeverity.High && l.PolicyAppliedAt >= identityReviewUntil))));
    }

    [HttpGet]
    public async Task<IActionResult> ActiveLiveExam(CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var examId = await AuthorizedLiveExams(teacherId, DateTime.Now)
            .OrderByDescending(e => e.ExamDate).ThenByDescending(e => e.ExamId)
            .Select(e => (int?)e.ExamId).FirstOrDefaultAsync(cancellationToken);
        return Json(new { examId });
    }

    [HttpGet]
    public async Task<IActionResult> Live(int? examId, CancellationToken cancellationToken = default)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var liveExams = await AuthorizedLiveExams(teacherId, DateTime.Now)
            .OrderByDescending(e => e.ExamDate).ThenByDescending(e => e.ExamId)
            .Select(e => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = e.ExamId.ToString(),
                Text = e.ExamTitle
            }).ToListAsync(cancellationToken);

        // Never silently switch the teacher to a different examination when a URL names one.
        if (examId.HasValue && !liveExams.Any(e => e.Value == examId.Value.ToString()))
        {
            if (!await TeacherEvidenceAccess.Exams(_db, teacherId)
                    .AnyAsync(e => e.ExamId == examId.Value, cancellationToken)) return NotFound();
            ViewBag.RoomClosed = true;
        }
        ViewBag.Exams = liveExams;
        ViewBag.ExamId = ViewBag.RoomClosed == true ? 0 :
            examId ?? (liveExams.Count > 0 ? int.Parse(liveExams[0].Value) : 0);
        ViewBag.RoomClosedExamId = ViewBag.RoomClosed == true ? examId : null;
        return View();
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LiveRoster(int examId, int page = 1, int? attemptId = null)
    {
        if (TeacherId is not string teacherId ||
            !await AuthorizedLiveExams(teacherId, DateTime.Now).AnyAsync(e => e.ExamId == examId))
            return NotFound();
        var attendance = await _db.ExamInvigilators.SingleOrDefaultAsync(i => i.ExamId == examId && i.TeacherId == TeacherId && i.IsActive && i.CanMonitor && i.RevokedAtUtc == null && i.AcceptedAtUtc != null);
        if (attendance is not null && (!attendance.LastMonitoringAtUtc.HasValue || attendance.LastMonitoringAtUtc < DateTime.UtcNow.AddMinutes(-1)))
        {
            attendance.FirstMonitoringAtUtc ??= DateTime.UtcNow;
            attendance.LastMonitoringAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        var owned = await _db.Exams.SingleOrDefaultAsync(e => e.ExamId == examId && e.CreatedBy == TeacherId);
        if (owned is not null && (!owned.CreatorLastMonitoringAtUtc.HasValue || owned.CreatorLastMonitoringAtUtc < DateTime.UtcNow.AddMinutes(-1)))
        {
            owned.CreatorFirstMonitoringAtUtc ??= DateTime.UtcNow;
            owned.CreatorLastMonitoringAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        var query = TeacherEvidenceAccess.Attempts(_db, TeacherId!).AsNoTracking().Where(a => a.ExamId == examId &&
            (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.ProctoringBlocked));
        var count = await query.CountAsync();
        if (attemptId is int target && await query.AnyAsync(a => a.AttemptId == target))
            page = 1 + await query.CountAsync(a => a.AttemptId < target) / 12;
        page = Math.Clamp(page, 1, Math.Max(1, (count + 11) / 12));
        var alertKinds = new[] { ProctoringAlertType.ReviewRequested, ProctoringAlertType.IdentityMismatch, ProctoringAlertType.FaceNotDetected,
            ProctoringAlertType.MultipleFaces, ProctoringAlertType.HeadAway, ProctoringAlertType.GazeAway,
            ProctoringAlertType.LookAway, ProctoringAlertType.LookingAwaySpeaking,
            ProctoringAlertType.AudioDetected, ProctoringAlertType.TalkingDetected, ProctoringAlertType.AnswerPromptSpeech,
            ProctoringAlertType.ElectronicDeviceDetected, ProctoringAlertType.ObjectObservation, ProctoringAlertType.CameraAccessLost,
            ProctoringAlertType.MicrophoneAccessLost, ProctoringAlertType.CameraMonitoringInterrupted,
            ProctoringAlertType.AiMonitoringUnavailable, ProctoringAlertType.AdditionalPersonObservation,
            ProctoringAlertType.TabSwitch, ProctoringAlertType.ExamPageHidden, ProctoringAlertType.FullScreenExit,
            ProctoringAlertType.ScreenShareLost, ProctoringAlertType.ScreenCaptureUnavailable };
        // Screening observations are NOT cheating strikes; they still merit a
        // short visible red review status on the correct candidate tile.
        var recentBrowserAfter = DateTime.UtcNow.AddSeconds(-15);
        // A warning must not move a candidate to another page and restart their feed.
        var rows = await query.OrderBy(a => a.AttemptId).Skip((page - 1) * 12).Take(12)
            .Select(a => new {
                a.AttemptId,
                a.StudentId,
                a.StrikeCount,
                blockingMode = a.Exam!.BlockingMode.ToString(),
                allowedWarnings = a.Exam.AllowedWarnings,
                version = a.StateVersion,
                identityAlert = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && l.AlertType == ProctoringAlertType.IdentityMismatch && l.Severity == ProctoringSeverity.High && l.PolicyProcessed && !l.IsReviewed &&
                    !_db.ProctoringLogs.Any(v => v.AttemptId == a.AttemptId && v.AlertType == ProctoringAlertType.IdentityVerified && v.LogId > l.LogId)).Any(),
                examName = a.Exam!.ExamTitle,
                incidents = _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId).OrderByDescending(s => s.SessionAttemptId).Select(s => s.ProctoringIncidentsJson).FirstOrDefault(),
                objectTracking = _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId).OrderByDescending(s => s.SessionAttemptId).Select(s => s.ObjectTrackingJson).FirstOrDefault(),
                health = _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId).OrderByDescending(s => s.SessionAttemptId).Select(s => s.DeviceHealthJson).FirstOrDefault(),
                name = a.Student!.FullName,
                status = a.Status.ToString(),
                activeBrowser = _db.ProctoringLogs.Any(l => l.AttemptId == a.AttemptId &&
                    l.IncidentKey == "browser" && l.CountsAsViolation && l.IncidentEndedAtUtc == null &&
                    l.AlertType != ProctoringAlertType.FullScreenExit && l.CreatedDate >= recentBrowserAfter),
                screenInterrupted = _db.ProctoringLogs.Any(l => l.AttemptId == a.AttemptId &&
                    l.IncidentKey == "browser" && l.IncidentEndedAtUtc == null && l.ParentIncidentId == null &&
                    (l.AlertType == ProctoringAlertType.ScreenShareLost || l.AlertType == ProctoringAlertType.ScreenCaptureUnavailable) &&
                    l.SessionAttemptId == _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId)
                        .Max(s => (int?)s.SessionAttemptId)),
                fullscreenObservation = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && l.ParentIncidentId == null &&
                    l.AlertType == ProctoringAlertType.FullScreenExit && l.IncidentKey == "browser" &&
                    l.SessionAttemptId == _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId)
                        .Max(s => (int?)s.SessionAttemptId))
                    .OrderByDescending(l => l.LogId).Select(l => new {
                        l.LogId, l.SessionAttemptId, l.CreatedDate, l.IncidentEndedAtUtc, l.MachineObservationsJson, l.RecoveryStatus,
                        l.CountsAsViolation
                    }).FirstOrDefault(),
                tabObservation = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && l.ParentIncidentId == null &&
                    (l.AlertType == ProctoringAlertType.ExamPageHidden || l.AlertType == ProctoringAlertType.TabSwitch) &&
                    l.SessionAttemptId == _db.ExamSessionAttempts.Where(s => s.AttemptId == a.AttemptId)
                        .Max(s => (int?)s.SessionAttemptId))
                    .OrderByDescending(l => l.LogId).Select(l => new {
                        l.LogId, l.SessionAttemptId, l.CreatedDate, l.IncidentEndedAtUtc, l.MachineObservationsJson, l.RecoveryStatus,
                        l.CountsAsViolation
                    }).FirstOrDefault(),
                speechAlert = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && !l.IsReviewed &&
                    l.AlertType == ProctoringAlertType.AnswerPromptSpeech && l.RecordingIncludesAudio &&
                    (l.VideoEvidencePath != null || l.RecordingPath != null))
                    .OrderByDescending(l => l.LogId).Select(l => new { l.LogId, l.CreatedDate, hasVideo = l.VideoEvidencePath != null }).FirstOrDefault(),
                alert = _db.ProctoringLogs.Where(l => l.AttemptId == a.AttemptId && !l.IsReviewed &&
                    alertKinds.Contains(l.AlertType) &&
                    // Preliminary identity recheck samples are NOT teacher cheating alerts.
                    (l.AlertType != ProctoringAlertType.IdentityMismatch || l.Severity == ProctoringSeverity.High))
                    .OrderByDescending(l => l.LogId).Select(l => new {
                        l.LogId,
                        kind = l.AlertType.ToString(),
                        l.Description,
                        l.CreatedDate,
                        l.FrameCapturedAt,
                        l.WarningNumber,
                        l.ConfidenceScore,
                        l.MatchedQuestionId,
                        hasScreenshot = l.ScreenshotPath != null,
                        hasVideo = l.VideoEvidencePath != null || l.RecordingPath != null,
                        l.CountsAsViolation,
                        attentionEnded = l.IncidentKey == "attention" && l.IncidentEndedAtUtc != null,
                        peopleEnded = (l.IncidentKey == "persons" && l.IncidentEndedAtUtc != null) ||
                            (l.IncidentKey == "persons:escalation" && _db.ProctoringLogs.Any(parent => parent.LogId == l.ParentIncidentId && parent.IncidentEndedAtUtc != null)),
                        absenceEnded = l.AlertType == ProctoringAlertType.FaceNotDetected &&
                            (l.IncidentEndedAtUtc != null || l.ParentIncidentId != null &&
                             _db.ProctoringLogs.Any(parent => parent.LogId == l.ParentIncidentId && parent.IncidentEndedAtUtc != null))
                    }).FirstOrDefault()
            }).ToListAsync(HttpContext.RequestAborted);
        var markers = rows.SelectMany(r => new[] { r.tabObservation, r.fullscreenObservation })
            .Where(l => l is not null).ToDictionary(l => l!.LogId,
                l => SessionRecordingMetadata.BrowserMarker(l!.MachineObservationsJson));
        var captureTimes = markers.Values.OfType<DateTimeOffset>().ToArray();
        var recordingsBySession = Enumerable.Empty<(int? SessionId, SessionRecordingMetadata? Media)>()
            .ToLookup(r => r.SessionId, r => r.Media);
        if (captureTimes.Length > 0)
        {
            // A valid segment covers at most 180 seconds. Only read files that could cover
            // an observation on this page, and verify the exact capture interval below.
            var earliest = captureTimes.Min().UtcDateTime.AddSeconds(-180);
            var latest = captureTimes.Max().UtcDateTime;
            var sessionIds = rows.SelectMany(r => new[] { r.tabObservation?.SessionAttemptId, r.fullscreenObservation?.SessionAttemptId })
                .Where(id => id.HasValue).Distinct().ToArray();
            var files = await _db.ProctoringLogs.AsNoTracking().Where(l =>
                    sessionIds.Contains(l.SessionAttemptId) && l.AlertType == ProctoringAlertType.SessionRecording &&
                    l.ModelName == SessionRecordingMetadata.ModelName && l.RecordingPath != null &&
                    l.FrameCapturedAt >= earliest && l.FrameCapturedAt <= latest)
                .Select(l => new { l.SessionAttemptId, l.MachineObservationsJson }).ToListAsync(HttpContext.RequestAborted);
            recordingsBySession = files.ToLookup(l => l.SessionAttemptId, l => SessionRecordingMetadata.Read(l.MachineObservationsJson));
        }

        return Json(new
        {
            rows = rows.Select(r => {
                var tab = r.tabObservation;
                var fullscreen = r.fullscreenObservation;
                return new {
                r.AttemptId,
                r.StudentId,
                r.StrikeCount,
                r.blockingMode,
                r.allowedWarnings,
                r.version,
                r.name,
                r.examName,
                r.status,
                r.alert,
                r.speechAlert,
                tabObservation = tab is null ? null : BrowserTile(tab.LogId, tab.SessionAttemptId, tab.CreatedDate,
                    tab.IncidentEndedAtUtc.HasValue, tab.RecoveryStatus, markers[tab.LogId], recordingsBySession),
                fullscreenObservation = fullscreen is null ? null : BrowserTile(fullscreen.LogId, fullscreen.SessionAttemptId,
                    fullscreen.CreatedDate, fullscreen.IncidentEndedAtUtc.HasValue, fullscreen.RecoveryStatus,
                    markers[fullscreen.LogId], recordingsBySession),
                tileStatus = r.identityAlert
                    ? new LiveTileStatus(r.status == "ProctoringBlocked" ? "identity_hold" : "active",
                        "Candidate identity mismatch — urgent teacher review")
                    : r.status == nameof(AttemptStatus.InProgress) &&
                        r.fullscreenObservation is { CountsAsViolation: true, IncidentEndedAtUtc: null }
                    ? new LiveTileStatus("active", "Active fullscreen exit")
                    : r.status == nameof(AttemptStatus.InProgress) && r.activeBrowser
                    ? new LiveTileStatus("active", "Active browser rule violation")
                    : LiveTileStatus.Evaluate(r.status, r.incidents, r.health, DateTime.UtcNow, r.objectTracking, r.screenInterrupted)
                };
            }),
            page,
            pages = Math.Max(1, (count + 11) / 12),
            count
        });
    }

    private static object BrowserTile(int id, int? sessionId, DateTime created, bool ended, string? recovery,
        DateTimeOffset? marker, ILookup<int?, SessionRecordingMetadata?> recordingsBySession)
    {
        var time = marker?.ToUnixTimeMilliseconds();
        var sources = time is long at
            ? recordingsBySession[sessionId].Where(m => m is { HasMedia: true } && m.StartedAt <= at && at < m.EndedAt)
                .Select(m => m!.Source).ToHashSet()
            : null;
        return new
        {
            logId = id, at = marker?.UtcDateTime ?? DateTime.SpecifyKind(created, DateTimeKind.Utc),
            ended, recovery, hasScreen = sources?.Contains("screen") == true, hasWebcam = sources?.Contains("camera") == true
        };
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseLive(int attemptId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300)
            return BadRequest("Describe what you reviewed in 10–300 characters.");
        if (TeacherId is not string teacherId || !await TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking().AnyAsync(a => a.AttemptId == attemptId, HttpContext.RequestAborted)) return NotFound();
        var state = await _states.ControlAsync(attemptId, teacherId, false, reason.Trim(), HttpContext.RequestAborted);
        return state is null ? Conflict("Attempt unavailable or no longer active.") : Json(state);
    }

    // Evidence navigation is exam-first. Do not derive candidate folders from log.StudentId.
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1,
        CancellationToken cancellationToken = default, int? examId = null)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (examId.HasValue)
        {
            if (!await TeacherEvidenceAccess.Exams(_db, teacherId)
                    .AnyAsync(e => e.ExamId == examId.Value, cancellationToken)) return NotFound();
            return RedirectToAction(nameof(Exam), new { examId = examId.Value });
        }

        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 120) search = search[..120];
        var examsQuery = TeacherEvidenceAccess.Exams(_db, teacherId).AsNoTracking();
        if (search is not null) examsQuery = examsQuery.Where(e => e.ExamTitle.Contains(search));
        const int pageSize = 12;
        var total = await examsQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (total + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, totalPages);
        var exams = await examsQuery.OrderByDescending(e => e.CreatedDate).ThenByDescending(e => e.ExamId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new { e.ExamId, e.ExamTitle, e.ExamDate, e.CreatedBy, e.Status, e.IsReusable })
            .ToListAsync(cancellationToken);
        var examIds = exams.Select(e => e.ExamId).ToArray();

        // Aggregate actual student attempts, not audit log user IDs or raw event volume.
        var attempted = await TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking()
            .Where(a => examIds.Contains(a.ExamId) && a.Status != AttemptStatus.NotStarted)
            .GroupBy(a => new { a.ExamId, a.StudentId })
            .Select(g => new {
                g.Key.ExamId,
                g.Key.StudentId,
                Count = g.Count(),
                Blocked = g.Any(a => a.Status == AttemptStatus.ProctoringBlocked)
            }).ToListAsync(cancellationToken);
        var reviews = await TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
            .Where(l => l.ExamId.HasValue && examIds.Contains(l.ExamId.Value))
            .GroupBy(l => new { ExamId = l.ExamId!.Value, l.StudentId })
            .Select(g => new { g.Key.ExamId, g.Key.StudentId, Pending = g.Count(l => !l.IsReviewed) })
            .ToListAsync(cancellationToken);
        var blockedKeys = attempted.Where(a => a.Blocked)
            .Select(a => (a.ExamId, a.StudentId)).ToHashSet();
        var rows = exams.Select(e => new TeacherEvidenceExamRow
        {
            ExamId = e.ExamId,
            Title = e.ExamTitle,
            ExamDate = e.ExamDate,
            IsOwner = e.CreatedBy == teacherId,
            IsReusable = e.IsReusable,
            Status = e.Status,
            StudentsTested = attempted.Count(a => a.ExamId == e.ExamId),
            TotalAttempts = attempted.Where(a => a.ExamId == e.ExamId).Sum(a => a.Count),
            BlockedStudents = attempted.Count(a => a.ExamId == e.ExamId && a.Blocked),
            FlaggedStudents = reviews.Count(r => r.ExamId == e.ExamId &&
                !blockedKeys.Contains((r.ExamId, r.StudentId))),
            PendingStudents = reviews.Count(r => r.ExamId == e.ExamId && r.Pending > 0)
        }).ToList();

        return View(new TeacherEvidenceIndexViewModel
        {
            Exams = rows,
            Search = search,
            Page = page,
            TotalPages = totalPages,
            TotalExams = total,
            TotalCandidates = rows.Sum(x => x.StudentsTested),
            BlockedStudents = rows.Sum(x => x.BlockedStudents),
            PendingStudents = rows.Sum(x => x.PendingStudents)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Exam(int examId, string? search, string category = "all",
        int page = 1, CancellationToken cancellationToken = default)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var exam = await TeacherEvidenceAccess.Exams(_db, teacherId).AsNoTracking()
            .Select(e => new { e.ExamId, e.ExamTitle, e.CreatedBy })
            .FirstOrDefaultAsync(e => e.ExamId == examId, cancellationToken);
        if (exam is null) return NotFound();

        category = category?.Trim().ToLowerInvariant() switch
        {
            "blocked" => "blocked",
            "flagged" => "flagged",
            "pending" => "pending",
            _ => "all"
        };
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search?.Length > 120) search = search[..120];
        var attemptsQuery = TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking()
            .Where(a => a.ExamId == examId && a.Status != AttemptStatus.NotStarted && a.Student != null);
        if (search is not null)
            attemptsQuery = attemptsQuery.Where(a => a.Student!.FullName.Contains(search) ||
                (a.Student.CandidateNumber != null && a.Student.CandidateNumber.Contains(search)) ||
                (a.Student.Email != null && a.Student.Email.Contains(search)));
        // Candidate identity always comes from ExamAttempt.Student, never ProctoringLog.Student.
        var attempts = await attemptsQuery.Select(a => new {
            a.AttemptId,
            a.StudentId,
            a.StartedAt,
            a.Status,
            Name = a.Student!.FullName,
            a.Student.CandidateNumber,
            a.Student.Email
        }).ToListAsync(cancellationToken);
        var allowedStudents = attempts.Select(a => a.StudentId).Distinct().ToArray();
        var reviews = await TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
            .Where(l => l.ExamId == examId && allowedStudents.Contains(l.StudentId))
            .GroupBy(l => l.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count(), Pending = g.Count(l => !l.IsReviewed) })
            .ToListAsync(cancellationToken);
        var reviewLookup = reviews.ToDictionary(x => x.StudentId, StringComparer.Ordinal);
        var evidenceCounts = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .Where(l => l.ExamId == examId && allowedStudents.Contains(l.StudentId) &&
                (l.ScreenshotPath != null || l.RecordingPath != null || l.VideoEvidencePath != null))
            .GroupBy(l => l.StudentId).Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var evidenceLookup = evidenceCounts.ToDictionary(x => x.StudentId, x => x.Count, StringComparer.Ordinal);
        var candidates = attempts.GroupBy(a => a.StudentId).Select(g => {
            reviewLookup.TryGetValue(g.Key, out var review);
            var latest = g.OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.AttemptId).First();
            return new TeacherEvidenceStudentRow
            {
                StudentId = g.Key,
                Name = latest.Name,
                CandidateNumber = latest.CandidateNumber,
                Email = latest.Email,
                AttemptCount = g.Count(),
                BlockedAttemptCount = g.Count(a => a.Status == AttemptStatus.ProctoringBlocked),
                ReviewEvents = review?.Count ?? 0,
                PendingEvents = review?.Pending ?? 0,
                EvidenceFiles = evidenceLookup.GetValueOrDefault(g.Key),
                LatestAt = latest.StartedAt
            };
        }).Where(c => c.IsBlocked || c.ReviewEvents > 0).ToList();
        // A blocked candidate is shown only in Blocked, not duplicated in Flagged.
        var blocked = candidates.Count(c => c.IsBlocked);
        var flagged = candidates.Count(c => !c.IsBlocked);
        var pending = candidates.Count(c => c.PendingEvents > 0);
        var filtered = category switch
        {
            "blocked" => candidates.Where(c => c.IsBlocked),
            "flagged" => candidates.Where(c => !c.IsBlocked),
            "pending" => candidates.Where(c => c.PendingEvents > 0),
            _ => candidates.AsEnumerable()
        };
        const int pageSize = 16;
        var total = filtered.Count();
        var totalPages = Math.Max(1, (total + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, totalPages);
        var ordered = filtered.OrderByDescending(c => c.IsBlocked)
            .ThenByDescending(c => c.PendingEvents).ThenByDescending(c => c.LatestAt)
            .ThenBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return View(new TeacherEvidenceExamViewModel
        {
            ExamId = examId,
            ExamTitle = exam.ExamTitle,
            IsOwner = exam.CreatedBy == teacherId,
            Search = search,
            Category = category,
            Page = page,
            TotalPages = totalPages,
            TotalStudents = candidates.Count,
            BlockedCount = blocked,
            FlaggedCount = flagged,
            PendingCount = pending,
            Students = ordered
        });
    }

    // Existing Candidate links without examId redirect to the most recent authorized exam.
    [HttpGet]
    public async Task<IActionResult> Candidate(string studentId, int? examId = null,
        CancellationToken cancellationToken = default)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (string.IsNullOrWhiteSpace(studentId)) return NotFound();
        var allowed = TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Status != AttemptStatus.NotStarted);
        if (!examId.HasValue)
        {
            var mostRecent = await allowed.OrderByDescending(a => a.StartedAt)
                .Select(a => (int?)a.ExamId).FirstOrDefaultAsync(cancellationToken);
            return mostRecent.HasValue
                ? RedirectToAction(nameof(Candidate), new { studentId, examId = mostRecent.Value })
                : NotFound();
        }
        var attempts = await allowed.Where(a => a.ExamId == examId.Value)
            .Include(a => a.Student).Include(a => a.Exam)
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.AttemptId)
            .ToListAsync(cancellationToken);
        if (attempts.Count == 0) return NotFound();
        var ids = attempts.Select(a => a.AttemptId).ToArray();
        var counts = await TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId.HasValue && ids.Contains(l.AttemptId.Value))
            .GroupBy(l => l.AttemptId!.Value)
            .Select(g => new { AttemptId = g.Key, Total = g.Count(), Pending = g.Count(l => !l.IsReviewed) })
            .ToListAsync(cancellationToken);
        var countLookup = counts.ToDictionary(c => c.AttemptId);
        var evidence = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId.HasValue && ids.Contains(l.AttemptId.Value) &&
                (l.ScreenshotPath != null || l.RecordingPath != null || l.VideoEvidencePath != null))
            .GroupBy(l => l.AttemptId!.Value)
            .Select(g => new { AttemptId = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var evidenceLookup = evidence.ToDictionary(c => c.AttemptId, c => c.Count);
        var sample = attempts[0];
        return View(new TeacherEvidenceCandidateViewModel
        {
            ExamId = sample.ExamId,
            ExamTitle = sample.Exam?.ExamTitle ?? "Examination",
            StudentId = studentId,
            StudentName = sample.Student?.FullName ?? "Candidate",
            CandidateNumber = sample.Student?.CandidateNumber,
            Email = sample.Student?.Email,
            Attempts = attempts.Select(a => new TeacherEvidenceAttemptRow
            {
                AttemptId = a.AttemptId,
                Status = a.Status,
                StartedAt = a.StartedAt,
                CompletedAt = a.CompletedAt,
                ReviewEvents = countLookup.GetValueOrDefault(a.AttemptId)?.Total ?? 0,
                PendingEvents = countLookup.GetValueOrDefault(a.AttemptId)?.Pending ?? 0,
                EvidenceFiles = evidenceLookup.GetValueOrDefault(a.AttemptId)
            }).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExamFolder(string studentId, int examId,
        bool pendingOnly = false, bool recordingsOnly = false, int page = 1,
        CancellationToken cancellationToken = default, int? incidentId = null, int? attemptId = null,
        string? alert = null, string? severity = null, bool savedMediaOnly = false)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (string.IsNullOrWhiteSpace(studentId)) return NotFound();
        var validAttempts = TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking()
            .Where(a => a.ExamId == examId && a.StudentId == studentId && a.Status != AttemptStatus.NotStarted);
        if (!attemptId.HasValue && incidentId.HasValue)
            attemptId = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
                .Where(l => l.LogId == incidentId.Value && l.ExamId == examId && l.StudentId == studentId)
                .Select(l => l.AttemptId).FirstOrDefaultAsync(cancellationToken);
        if (!attemptId.HasValue)
            attemptId = await validAttempts.OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.AttemptId)
                .Select(a => (int?)a.AttemptId).FirstOrDefaultAsync(cancellationToken);
        if (!attemptId.HasValue) return NotFound();
        var selected = await validAttempts.Include(a => a.Student).Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId.Value, cancellationToken);
        if (selected?.Student is null || selected.Exam is null) return NotFound();

        var all = TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId == selected.AttemptId && l.ExamId == examId && l.StudentId == studentId);
        var summary = await all.GroupBy(l => 1).Select(g => new {
            Events = g.Count(l => l.AlertType != ProctoringAlertType.AbsenceEvidence &&
                l.AlertType != ProctoringAlertType.PersonEvidence &&
                l.AlertType != ProctoringAlertType.ObjectEvidence &&
                l.AlertType != ProctoringAlertType.BrowserEvidence),
            Violations = g.Count(l => l.CountsAsViolation),
            Files = g.Count(l => l.ScreenshotPath != null || l.VideoEvidencePath != null || l.RecordingPath != null),
            High = g.Count(l => l.Severity == ProctoringSeverity.High),
            Latest = g.Max(l => l.CreatedDate)
        }).FirstOrDefaultAsync(cancellationToken);
        var pending = await TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
            .CountAsync(l => l.AttemptId == selected.AttemptId && l.ExamId == examId && l.StudentId == studentId && !l.IsReviewed, cancellationToken);
        ViewBag.PendingObservations = await TeacherEvidenceAccess.ObservationIncidents(_db, teacherId).AsNoTracking()
            .CountAsync(l => l.AttemptId == selected.AttemptId && l.ExamId == examId && l.StudentId == studentId && !l.IsReviewed, cancellationToken);
        var visible = all.Where(l => l.AlertType != ProctoringAlertType.AbsenceEvidence &&
            l.AlertType != ProctoringAlertType.PersonEvidence &&
            l.AlertType != ProctoringAlertType.ObjectEvidence &&
            l.AlertType != ProctoringAlertType.BrowserEvidence && l.ModelName != "Object policy audit");
        if (pendingOnly)
            visible = TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
                .Where(l => l.AttemptId == selected.AttemptId && l.ExamId == examId && l.StudentId == studentId && !l.IsReviewed);
        visible = visible.Where(l => recordingsOnly
            ? l.AlertType == ProctoringAlertType.SessionRecording
            : l.AlertType != ProctoringAlertType.SessionRecording);
        if (savedMediaOnly)
            visible = all.Where(l => (l.ParentIncidentId == null || l.AlertType == ProctoringAlertType.SessionRecording) &&
                (l.ScreenshotPath != null || l.VideoEvidencePath != null || l.RecordingPath != null ||
                 all.Any(asset => asset.ParentIncidentId == l.LogId &&
                     (asset.ScreenshotPath != null || asset.VideoEvidencePath != null || asset.RecordingPath != null))));
        ViewBag.SavedMediaOnly = savedMediaOnly;
        if (!string.IsNullOrWhiteSpace(alert) &&
            Enum.TryParse<ProctoringAlertType>(alert, true, out var selectedAlert) &&
            Enum.IsDefined(selectedAlert))
            visible = visible.Where(l => l.AlertType == selectedAlert);
        else alert = null;
        if (!string.IsNullOrWhiteSpace(severity) &&
            Enum.TryParse<ProctoringSeverity>(severity, true, out var selectedSeverity) &&
            Enum.IsDefined(selectedSeverity))
            visible = visible.Where(l => l.Severity == selectedSeverity);
        else severity = null;
        ViewBag.SelectedAlert = alert;
        ViewBag.SelectedSeverity = severity;
        visible = visible.Include(l => l.Student).Include(l => l.Exam)
            .Include(l => l.Attempt).Include(l => l.SessionAttempt).ThenInclude(s => s!.Session);
        const int pageSize = 40;
        if (incidentId.HasValue)
        {
            var target = await visible.Where(l => l.LogId == incidentId.Value)
                .Select(l => new { l.LogId, l.CreatedDate, l.AlertType }).FirstOrDefaultAsync(cancellationToken);
            if (target is null) return NotFound();
            if (target.AlertType is ProctoringAlertType.ExamPageHidden or ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit)
                ViewBag.RecordingMarker = target.LogId;
            page = 1 + await visible.CountAsync(l => l.CreatedDate > target.CreatedDate ||
                (l.CreatedDate == target.CreatedDate && l.LogId > target.LogId), cancellationToken) / pageSize;
        }
        var pages = Math.Max(1, (await visible.CountAsync(cancellationToken) + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pages);
        var logs = await visible.OrderByDescending(l => l.CreatedDate).ThenByDescending(l => l.LogId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var logIds = logs.Select(l => l.LogId).ToArray();
        ViewData["ReviewableIds"] = (await TeacherEvidenceAccess.ObservationIncidents(_db, teacherId)
            .Where(l => logIds.Contains(l.LogId))
            .Select(l => l.LogId).ToListAsync(cancellationToken)).ToHashSet();
        var parentIds = logIds;
        ViewData["AbsenceEvidence"] = await all.Where(l => l.ParentIncidentId.HasValue &&
            parentIds.Contains(l.ParentIncidentId.Value)).OrderBy(l => l.FrameCapturedAt)
            .ThenBy(l => l.LogId).ToListAsync(cancellationToken);
        ViewBag.RecordingPage = page;
        ViewBag.IsOwner = selected.Exam.CreatedBy == teacherId;
        ViewBag.RecordingPages = pages;
        ViewBag.RecordingsOnly = recordingsOnly;
        var folder = new ExamProctoringFolderViewModel
        {
            ExamId = examId,
            ExamTitle = selected.Exam.ExamTitle,
            TeacherName = User.Identity?.Name ?? "Teacher",
            Audience = selected.Exam.Audience,
            Purpose = selected.Exam.Purpose,
            AttemptCount = 1,
            AlertCount = summary?.Events ?? 0,
            ViolationCount = summary?.Violations ?? 0,
            PendingCount = pending,
            EvidenceCount = summary?.Files ?? 0,
            HighSeverityCount = summary?.High ?? 0,
            LatestEventAt = summary?.Latest
        };
        return View(new ProctoringExamFolderPageViewModel
        {
            AttemptId = selected.AttemptId,
            AttemptStatus = selected.Status,
            BlockedAttemptIds = selected.Status == AttemptStatus.ProctoringBlocked
                ? new[] { selected.AttemptId } : Array.Empty<int>(),
            Candidate = BuildCandidateFolder(selected.Student, new[] { folder }),
            ExamFolder = folder,
            Logs = logs,
            TeacherScope = true,
            PendingOnly = pendingOnly
        });
    }

    // The storage key is never supplied by clients. The DB record + real student
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RecordingTimeline(int attemptId, CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (!await TeacherEvidenceAccess.Attempts(_db, teacherId).AnyAsync(a => a.AttemptId == attemptId, cancellationToken))
            return NotFound();

        var sessions = await _db.ExamSessionAttempts.AsNoTracking().Where(s => s.AttemptId == attemptId)
            .OrderBy(s => s.SessionAttemptId).Select(s => new { id = s.SessionAttemptId, start = s.StartedAt, end = s.CompletedAt })
            .ToListAsync(cancellationToken);
        var rows = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId == attemptId &&
                (l.AlertType == ProctoringAlertType.SessionRecording && l.ModelName == SessionRecordingMetadata.ModelName ||
                    l.AlertType == ProctoringAlertType.TabSwitch || l.AlertType == ProctoringAlertType.ExamPageHidden ||
                    l.AlertType == ProctoringAlertType.FullScreenExit))
            .OrderBy(l => l.FrameCapturedAt).ThenBy(l => l.LogId)
            .Select(l => new { l.LogId, l.SessionAttemptId, l.AlertType, l.MachineObservationsJson, l.RecordingPath,
                l.IncidentEndedAtUtc, l.ObservedDurationSeconds, l.RecoveryStatus })
            .ToListAsync(cancellationToken);
        return Json(new
        {
            sessions = sessions.Select(s => new { s.id, start = s.start?.ToUniversalTime(), end = s.end?.ToUniversalTime() }),
            segments = rows.Where(row => row.AlertType == ProctoringAlertType.SessionRecording)
                .Select(row => new { row.LogId, row.SessionAttemptId, metadata = SessionRecordingMetadata.Read(row.MachineObservationsJson),
                    url = row.RecordingPath is null ? null : Url.Action(nameof(Evidence), new { id = row.LogId, kind = "recording" }) })
                .Where(row => row.metadata is not null),
            markers = rows.Where(row => row.AlertType != ProctoringAlertType.SessionRecording)
                .Select(row => new {
                    row.LogId, row.SessionAttemptId,
                    kind = row.AlertType == ProctoringAlertType.FullScreenExit ? "Fullscreen exit" : "Tab switching",
                    at = SessionRecordingMetadata.BrowserMarker(row.MachineObservationsJson),
                    returnedAt = row.RecoveryStatus == "Returned"
                        ? SessionRecordingMetadata.BrowserReturn(row.MachineObservationsJson, row.IncidentEndedAtUtc) : null,
                    monitoringEnded = row.RecoveryStatus == "MonitoringEnded",
                    ended = row.IncidentEndedAtUtc.HasValue,
                    durationSeconds = row.ObservedDurationSeconds
                })
        });
    }

    // attempt + exam monitor permission are checked BEFORE opening encrypted media.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Evidence(int id, string? kind, CancellationToken cancellationToken,
        bool download = false)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (kind is not ("screenshot" or "video" or "recording")) return NotFound();
        var incident = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .FirstOrDefaultAsync(l => l.LogId == id, cancellationToken);
        if (incident is null) return NotFound();
        var key = EvidenceSharing.Key(incident, kind);
        if (string.IsNullOrWhiteSpace(key)) return NotFound();
        var file = await _evidence.OpenReadAsync(key, cancellationToken);
        if (file is null) return NotFound();
        // Audit successful access, including selected media type, without logging
        // any original media bytes or making the storage path public.
        try { await EvidenceAccessAudit.RecordAsync(_db, incident, teacherId, kind, download, cancellationToken); }
        catch { await file.Stream.DisposeAsync(); throw; }
        Response.Headers.CacheControl = "private,no-store,max-age=0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var extension = Path.GetExtension(key).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webm" or ".mp4" or ".wav" or ".ogg"))
            extension = ".bin";
        return File(file.Stream, file.ContentType,
            download ? $"evidence-{id}{extension}" : null, enableRangeProcessing: true);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReviewed(int id, string studentId, int examId,
        CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var incident = await TeacherEvidenceAccess.ObservationIncidents(_db, teacherId)
            .FirstOrDefaultAsync(l => l.LogId == id && l.StudentId == studentId && l.ExamId == examId,
                cancellationToken);
        if (incident is null) return NotFound();
        incident.IsReviewed = true;
        await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Observation marked reviewed. This does not publish a result.";
        return RedirectToAction(nameof(ExamFolder), new
        {
            studentId,
            examId,
            attemptId = incident.AttemptId
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExamReviewed(string studentId, int examId,
        CancellationToken cancellationToken, int? attemptId = null)
    {
        if (TeacherId is not string teacherId) return Challenge();
        var attempts = TeacherEvidenceAccess.Attempts(_db, teacherId)
            .Where(a => a.ExamId == examId && a.StudentId == studentId);
        if (attemptId.HasValue) attempts = attempts.Where(a => a.AttemptId == attemptId.Value);
        if (!await attempts.AnyAsync(cancellationToken)) return NotFound();
        var relevant = TeacherEvidenceAccess.ObservationIncidents(_db, teacherId)
            .Where(l => l.ExamId == examId && l.StudentId == studentId && !l.IsReviewed);
        if (attemptId.HasValue) relevant = relevant.Where(l => l.AttemptId == attemptId.Value);
        var logs = await relevant.ToListAsync(cancellationToken);
        foreach (var log in logs) log.IsReviewed = true;
        if (logs.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{logs.Count} observation(s) reviewed. Result release remains a separate approval step.";
        return RedirectToAction(nameof(Candidate), new { studentId, examId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnblockAttempt(int attemptId, string studentId, int examId,
        string reason, CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300)
            return BadRequest("Provide a review reason in 10–300 characters.");
        if (!await TeacherEvidenceAccess.Attempts(_db, teacherId).AsNoTracking()
                .AnyAsync(a => a.AttemptId == attemptId && a.StudentId == studentId &&
                    a.ExamId == examId && a.Status == AttemptStatus.ProctoringBlocked, cancellationToken))
            return NotFound();
        var lastBlockId = await _db.ProctoringLogs.AsNoTracking()
            .Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
            .OrderByDescending(l => l.LogId).Select(l => l.LogId).FirstOrDefaultAsync(cancellationToken);
        var pendingAppealId = await TeacherEvidenceAccess.Incidents(_db, teacherId).AsNoTracking()
            .Where(l => l.AttemptId == attemptId && l.ModelName == ReviewRequestRules.RequestModel &&
                l.AlertType == ProctoringAlertType.ReviewRequested && !l.IsReviewed &&
                l.Attempt != null &&
                (lastBlockId > 0 ? l.LogId > lastBlockId && l.ModelVersion != null &&
                    l.ModelVersion.StartsWith("lock:") : l.ModelVersion == "lock:" + l.Attempt.StateVersion))
            .OrderByDescending(l => l.LogId).Select(l => (int?)l.LogId)
            .FirstOrDefaultAsync(cancellationToken);
        if (pendingAppealId.HasValue)
        {
            TempData["Error"] = "Resolve this student's pending appeal from the Review Requests page to record the official decision.";
            return RedirectToAction("Details", "ReviewRequests", new { id = pendingAppealId.Value });
        }
        var state = await _states.ControlAsync(attemptId, teacherId, true, reason.Trim(), cancellationToken);
        if (state is null) return Conflict("Attempt unavailable or cannot be resumed.");
        TempData["Success"] = "Examination resumed according to the configured rules.";
        return RedirectToAction(nameof(ExamFolder), new { studentId, examId, attemptId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseAfterReview(int logId, string reason,
        CancellationToken cancellationToken)
    {
        if (TeacherId is not string teacherId) return Challenge();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300)
            return BadRequest("Provide a review reason in 10–300 characters.");
        var incident = await TeacherEvidenceAccess.ReviewableIncidents(_db, teacherId).AsNoTracking()
            .Where(l => l.LogId == logId && l.AttemptId.HasValue)
            .Select(l => new { l.AttemptId, l.StudentId, l.ExamId })
            .FirstOrDefaultAsync(cancellationToken);
        if (incident?.AttemptId is null || incident.ExamId is null) return NotFound();
        var state = await _states.ControlAsync(incident.AttemptId.Value, teacherId, false,
            reason.Trim(), cancellationToken);
        if (state is null) return Conflict("Attempt unavailable or no longer active.");
        return RedirectToAction(nameof(ExamFolder), new
        {
            studentId = incident.StudentId,
            examId = incident.ExamId,
            attemptId = incident.AttemptId
        });
    }

    private static CandidateProctoringFolderViewModel BuildCandidateFolder(ApplicationUser student, IEnumerable<ExamProctoringFolderViewModel> exams)
    {
        var folders = exams.ToList();
        return new CandidateProctoringFolderViewModel
        {
            StudentId = student.Id,
            CandidateName = student.FullName,
            CandidateNumber = student.CandidateNumber ?? "Not assigned",
            Email = student.Email ?? string.Empty,
            ProfileImage = student.ProfileImage,
            ExamFolderCount = folders.Count,
            AlertCount = folders.Sum(x => x.AlertCount),
            ViolationCount = folders.Sum(x => x.ViolationCount),
            PendingCount = folders.Sum(x => x.PendingCount),
            EvidenceCount = folders.Sum(x => x.EvidenceCount),
            HighSeverityCount = folders.Sum(x => x.HighSeverityCount),
            LatestEventAt = folders.Count == 0 ? null : folders.Max(x => x.LatestEventAt)
        };
    }
}

