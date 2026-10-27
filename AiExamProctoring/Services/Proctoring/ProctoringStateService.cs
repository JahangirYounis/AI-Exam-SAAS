using System.Data;
using System.Text.Json;
using AiExamProctoring.Data;
using AiExamProctoring.Hubs;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiExamProctoring.Services.Proctoring;

public sealed record ProctoringState(int AttemptId, int? SessionAttemptId, string Status,
    int StrikeCount, long Version, string? ViolationType, int? EventId,
    int CurrentQuestionIndex, int? CurrentQuestionId, int RemainingTimeSeconds,
    string? SelectedAnswersJSON, string ResumeUrl, string? EventCode = null, string? CandidateName = null, string BlockingMode = "BOTH", string? Reason = null, string? RiskLevel = null, DateTime? OccurredAt = null, bool HasScreenshot = false, bool HasRecording = false, string? Transcript = null, decimal? SemanticSimilarity = null, int? MatchedQuestionId = null, string ViolationAction = "LockForReview")
{
    public int WarningCount => Math.Clamp(StrikeCount, 0, 3);
}

public interface IProctoringStateService
{
    Task<DeviceHealthReply?> HealthAsync(int attemptId, int sessionId, string studentId, string issue, CancellationToken ct = default);
    Task<ProctoringState?> GetAsync(int attemptId, CancellationToken ct = default);
    Task ApplyLogAsync(int logId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> ObserveAsync(int attemptId, int sessionId, string studentId, DateTime sampleAt,
        IReadOnlyList<IncidentObservation> observations, CancellationToken ct = default);
    Task NotifySpeechContextAsync(int logId, CancellationToken ct = default);
    Task<ProctoringState?> ControlAsync(int attemptId, string teacherId, bool resume, string reason, CancellationToken ct = default);
}

// SQL Server is authoritative. Inference and media transfer never hold a database lock.
public sealed partial class ProctoringStateService(ApplicationDbContext db, IHubContext<ProctoringHub> hub,
    ILogger<ProctoringStateService> logger, IOptions<ProctoringOptions>? options = null) : IProctoringStateService
{
    private readonly ProctoringOptions settings = options?.Value ?? new();

    public async Task<IReadOnlyList<int>> ObserveAsync(int attemptId, int sessionId, string studentId, DateTime sampleAt,
        IReadOnlyList<IncidentObservation> observations, CancellationToken ct = default)
    {
        List<int> ids = [];
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            ids.Clear();
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, attemptId, ct);
            var s = await db.ExamSessionAttempts.Include(x => x.Attempt).Include(x => x.Session)
                .SingleOrDefaultAsync(x => x.SessionAttemptId == sessionId && x.AttemptId == attemptId && x.Attempt!.StudentId == studentId, ct);
            var a = s?.Attempt;
            var now = sampleAt.ToUniversalTime();
            if (a is null || s is null || a.Status != AttemptStatus.InProgress || s.Status != AttemptStatus.InProgress ||
                Remaining(s) <= 0 || a.TeacherResumedAtUtc.HasValue && now <= a.TeacherResumedAtUtc.Value) return;
            var episodes = string.IsNullOrWhiteSpace(s.ProctoringIncidentsJson)
                ? new Dictionary<string, IncidentEpisode>()
                : JsonSerializer.Deserialize<Dictionary<string, IncidentEpisode>>(s.ProctoringIncidentsJson)!;
            var accepted = new List<ProctoringLog>();
            foreach (var observation in observations)
            {
                // Repeated semantic completions of one server-screened utterance cannot
                // emit another event, even when inference completes after cooldown.
                if (observation.Log.ClientEventId is string eventKey &&
                    await db.ProctoringLogs.AnyAsync(x => x.ClientEventId == eventKey, ct)) continue;
                var persistence = observation.Key switch
                {
                    "attention" => settings.AttentionPersistenceSeconds,
                    "persons" => settings.MultiplePersonPersistenceSeconds,
                    "absence" => settings.FaceAbsencePersistenceSeconds,
                    "objects" => settings.ObjectPersistenceSeconds,
                    "mouth" => .5,
                    "speech" => 0, // already temporally confirmed by VAD within the audio clip
                    _ => throw new ArgumentException("Unsupported incident source.")
                };
                if (!episodes.TryGetValue(observation.Key, out var episode)) episodes[observation.Key] = episode = new();
                // Continued speech or a retained banned object may re-emit after a ten-second warning interval.
                // This is measured on fresh server VAD observations, not browser counters.
                if ((observation.Key == "speech" || observation.Key == "objects") && observation.Active == true && episode.Emitted &&
                    episode.LastEmittedUtc.HasValue && (now - episode.LastEmittedUtc.Value).TotalSeconds >= 10)
                    episode.Emitted = false;
                if (!IncidentLifecycle.Observe(episode, observation.Active, now, Math.Clamp(persistence, 0, 60),
                    Math.Clamp(settings.IncidentRecoverySeconds, .1, 30), Math.Clamp(observation.Key == "objects" ? Math.Max(60, settings.IncidentMaximumSampleGapSeconds) : settings.IncidentMaximumSampleGapSeconds, 2, 120),
                    Math.Clamp(settings.IncidentCooldownSeconds, 0, 120))) continue;
                if (observation.Key == "mouth") continue; // Supporting signal only, never a strike.
                var log = observation.Log;
                log.LogId = 0;
                log.StudentId = studentId; log.ExamId = a.ExamId; log.AttemptId = attemptId; log.SessionAttemptId = sessionId;
                log.IncidentKey = observation.Key; log.FrameCapturedAt = sampleAt;
                log.PolicyPending = true; log.PolicyProcessed = false; log.CountsAsViolation = false;
                log.CreatedDate = DateTime.Now;
                log.ClientEventId ??= Guid.NewGuid().ToString("N");
                db.ProctoringLogs.Add(log); accepted.Add(log);
            }
            s.ProctoringIncidentsJson = JsonSerializer.Serialize(episodes);
            await db.SaveChangesAsync(ct);
            ids.AddRange(accepted.Select(x => x.LogId));
            await tx.CommitAsync(ct);
        });
        // Recovery worker also applies pending logs after process interruption.
        foreach (var id in ids) await ApplyLogAsync(id, ct);
        return ids;
    }
    public static bool IsStandard(ProctoringAlertType type) => type is
        ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit or
        ProctoringAlertType.FaceNotDetected or ProctoringAlertType.MultipleFaces or ProctoringAlertType.HeadAway or
        ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway or
        ProctoringAlertType.ElectronicDeviceDetected or ProctoringAlertType.AnswerPromptSpeech or ProctoringAlertType.TalkingDetected;

    public static bool IsCritical(ProctoringLog log) => log.AlertType == ProctoringAlertType.IdentityMismatch &&
        log.ConfidenceScore.HasValue && 1m - log.ConfidenceScore.Value > .45m;

    public static async Task LockAttemptAsync(ApplicationDbContext context, int id, CancellationToken ct)
    {
        if (context.Database.IsSqlServer())
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT AttemptId FROM ExamAttempts WITH (UPDLOCK, HOLDLOCK) WHERE AttemptId = {id}", ct);
    }

    public async Task<ProctoringState?> GetAsync(int attemptId, CancellationToken ct = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAttemptAsync(db, attemptId, ct);
        var attempt = await db.ExamAttempts.AsNoTracking().Include(a => a.Exam).SingleOrDefaultAsync(a => a.AttemptId == attemptId, ct);
        if (attempt is null) return null;
        var session = await db.ExamSessionAttempts.AsNoTracking().Include(s => s.Session)
            .Where(s => s.AttemptId == attemptId).OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
        var latest = await db.ProctoringLogs.AsNoTracking().Where(l => l.AttemptId == attemptId && (l.CountsAsViolation || attempt.Status == AttemptStatus.ProctoringBlocked && l.Description != null && l.Description.StartsWith("[DEVICE_HOLD]")))
            .OrderByDescending(l => l.LogId).FirstOrDefaultAsync(ct);
        var state = State(attempt, session, latest) with {
            CandidateName = await db.Users.Where(u => u.Id == attempt.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) };
        await tx.CommitAsync(ct);
        return state;
        });
    }

    private static ProctoringState State(ExamAttempt a, ExamSessionAttempt? s, ProctoringLog? log = null) => new(
        a.AttemptId, s?.SessionAttemptId, a.Status.ToString(), a.StrikeCount, a.StateVersion,
        log?.AlertType.ToString(), log?.LogId, s?.CurrentQuestionIndex ?? 0, s?.LastQuestionId,
        s is null ? 0 : s.FrozenAtUtc.HasValue ? s.RemainingTimeSeconds ?? 0 : Remaining(s),
        s?.SelectedAnswersJSON, $"/Student/Exam/Resume?attemptId={a.AttemptId}", EventCode(log), a.Student?.FullName, (a.Exam?.BlockingMode ?? ExamBlockingMode.BOTH).ToString(), log?.Description, log?.Severity.ToString(), log?.FrameCapturedAt ?? log?.CreatedDate, log?.ScreenshotPath is not null, log?.RecordingPath is not null || log?.VideoEvidencePath is not null, log?.SpeechTranscript, log?.SemanticSimilarity, log?.MatchedQuestionId, AutomaticViolationAction.LockForReview.ToString());

    private static string? EventCode(ProctoringLog? log)
    {
        var text = log?.Description;
        if (text?.StartsWith('[') != true) return null;
        var end = text.IndexOf(']');
        if (end is < 2 or > 40) return null;
        var code = text[1..end];
        return code.All(c => char.IsAsciiLetterUpper(c) || c == '_') ? code : null;
    }

    private static int Remaining(ExamSessionAttempt s) => s.FrozenAtUtc.HasValue ? s.RemainingTimeSeconds ?? 0 : Math.Max(0, (int)Math.Ceiling(
        ((s.StartedAt ?? DateTime.Now).AddMinutes(s.Session?.DurationMinutes ?? 0) - DateTime.Now).TotalSeconds));

    private async Task FreezeAsync(ExamAttempt a, ExamSessionAttempt s, string reason, CancellationToken ct)
    {
        s.RemainingTimeSeconds = Remaining(s);
        s.FrozenAtUtc = DateTime.UtcNow;
        s.SelectedAnswersJSON = JsonSerializer.Serialize(await db.StudentAnswers.AsNoTracking()
            .Where(x => x.SessionAttemptId == s.SessionAttemptId).ToDictionaryAsync(x => x.QuestionId, x => x.SelectedOption, ct));
        a.Status = s.Status = AttemptStatus.ProctoringBlocked;
        db.ProctoringLogs.Add(Audit(a, s, ProctoringAlertType.ExamBlocked, reason));
    }

    private static ProctoringLog Audit(ExamAttempt a, ExamSessionAttempt s, ProctoringAlertType kind, string reason) => new()
    {
        StudentId = a.StudentId, ExamId = a.ExamId, AttemptId = a.AttemptId, SessionAttemptId = s.SessionAttemptId,
        AlertType = kind, Description = reason, ModelName = "Proctoring policy", CountsAsViolation = false,
        PolicyProcessed = true, IsReviewed = true, ClientEventId = Guid.NewGuid().ToString("N"), CreatedDate = DateTime.Now
    };

    public async Task ApplyLogAsync(int logId, CancellationToken ct = default)
    {
        var id = await db.ProctoringLogs.AsNoTracking().Where(l => l.LogId == logId).Select(l => l.AttemptId).SingleOrDefaultAsync(ct);
        if (!id.HasValue) return;
        ProctoringState? state = null;
        string? owner = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            state = null;
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, id.Value, ct);
            var log = await db.ProctoringLogs.SingleAsync(l => l.LogId == logId, ct);
            var promoteSoftSpeech = log.ModelName == "Speech soft reminder" && !log.CountsAsViolation &&
                log.AlertType == ProctoringAlertType.AnswerPromptSpeech && log.Severity == ProctoringSeverity.High;
            if ((log.PolicyProcessed || !log.PolicyPending) && !promoteSoftSpeech) return;
            log.PolicyProcessed = true;
            var a = await db.ExamAttempts.Include(a => a.Exam).SingleAsync(a => a.AttemptId == id.Value, ct);
            var s = await db.ExamSessionAttempts.Include(s => s.Session).SingleOrDefaultAsync(s => s.SessionAttemptId == log.SessionAttemptId, ct);
            var critical = IsCritical(log);
            if (critical && await db.ProctoringLogs.AnyAsync(x => x.AttemptId == a.AttemptId &&
                x.AlertType == ProctoringAlertType.IdentityVerified && x.FrameCapturedAt > log.FrameCapturedAt, ct))
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            // A global 10-second episode window coalesces concurrent head/gaze/object detections.
            // Identity mismatch always bypasses this window. Old inference cannot punish a resumed attempt.
            if (s is not null && a.Status == AttemptStatus.InProgress && s.Status == AttemptStatus.InProgress &&
                Remaining(s) > 0 && (a.TeacherResumedAtUtc is null || log.FrameCapturedAt?.ToUniversalTime() > a.TeacherResumedAtUtc) &&
                (critical || IsStandard(log.AlertType) &&
                    (log.IncidentKey is not null || a.LastViolationAtUtc is null || DateTime.UtcNow - a.LastViolationAtUtc >= TimeSpan.FromSeconds(10))))
            {
                // Every confirmed standard violation shares the exam-attempt counter,
                // including the first confirmed candidate-speaking event.
                log.CountsAsViolation = true;
                a.TotalViolations++;
                a.LastViolationAtUtc = DateTime.UtcNow;
                a.StateVersion++;
                if (critical) a.HighSeverityViolations++;
                else a.StrikeCount = a.StrikeCount == int.MaxValue ? int.MaxValue : a.StrikeCount + 1;
                log.WarningNumber = !critical && a.StrikeCount <= 3 ? a.StrikeCount : null;
                if (BlockingPolicy.ShouldAutoLock(a.Exam!.BlockingMode, a.StrikeCount, critical)) {
                    await FreezeAsync(a, s, critical ? "Identity confirmation hold: embedding distance exceeds 0.45. Teacher review required; no misconduct determination." : "Review hold after three warnings and a fourth observation. Teacher review required; no misconduct determination.", ct);
                }
                owner = a.Exam!.CreatedBy;
                state = State(a, s, log) with {
                    CandidateName = await db.Users.Where(u => u.Id == a.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) };
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        if (state is not null) await PublishAsync(state, owner!, state.EventCode == "SPEECH_REMINDER" ? "SpeechReminder" : state.Status == nameof(AttemptStatus.ProctoringBlocked) ? "ExamLocked" : state.Status == nameof(AttemptStatus.Completed) ? "ExamSubmitted" : "StrikeIssued");
    }

    public async Task<ProctoringState?> ControlAsync(int attemptId, string teacherId, bool resume, string reason, CancellationToken ct = default)
    {
        var adminResume = resume && await (from membership in db.UserRoles
            join role in db.Roles on membership.RoleId equals role.Id
            where membership.UserId == teacherId && role.Name == "Admin"
            select membership.UserId).AnyAsync(ct);
        ProctoringState? state = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            state = null;
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, attemptId, ct);
            var a = await db.ExamAttempts.Include(a => a.Exam).SingleOrDefaultAsync(a => a.AttemptId == attemptId && (adminResume || a.Exam!.CreatedBy == teacherId || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == teacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)), ct);
            if (a is null || (!resume && !BlockingPolicy.CanManuallyBlock(a.Exam!.BlockingMode)) || a.Status != (resume ? AttemptStatus.ProctoringBlocked : AttemptStatus.InProgress)) return;
            var s = await db.ExamSessionAttempts.Include(s => s.Session).Where(s => s.AttemptId == attemptId && s.Status == a.Status)
                .OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
            if (s is null) return;
            if (resume)
            {
                if (s.FrozenAtUtc.HasValue)
                    s.StartedAt = DateTime.Now.AddSeconds(s.RemainingTimeSeconds ?? 0).AddMinutes(-s.Session!.DurationMinutes);
                else // Compatible with holds saved before this migration.
                {
                    var blockedAt = await db.ProctoringLogs.Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
                        .OrderByDescending(l => l.LogId).Select(l => (DateTime?)l.CreatedDate).FirstOrDefaultAsync(ct);
                    if (blockedAt.HasValue && s.StartedAt.HasValue) s.StartedAt += DateTime.Now - blockedAt.Value;
                }
                s.RemainingTimeSeconds ??= Remaining(s);
                s.FrozenAtUtc ??= DateTime.UtcNow;
                s.ProctoringIncidentsJson = null;
                s.DeviceHealthJson = null;
                a.Status = s.Status = AttemptStatus.InProgress;
                a.StrikeCount = 0;
                a.LastViolationAtUtc = null;
                a.TeacherResumedAtUtc = DateTime.UtcNow;
                db.ProctoringLogs.Add(Audit(a, s, ProctoringAlertType.ExamUnblocked, $"{(adminResume ? "Administrator" : "Teacher")} {teacherId} authorized resume. {reason}"));
                db.StudentNotifications.Add(new StudentNotification { StudentId = a.StudentId, ExamId = a.ExamId, Title = "Your teacher has unblocked your exam", Message = "You may resume your exam from the saved question and remaining time. Open your examinations and choose Resume. The entry window does not prevent this approved return.", Type = "ExamResumed" });
            }
            else await FreezeAsync(a, s, $"Teacher {teacherId}: {reason}", ct);
            var actorName = await db.Users.Where(u => u.Id == teacherId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? teacherId;
            foreach (var audit in db.ChangeTracker.Entries<ProctoringLog>().Where(e => e.State == EntityState.Added && e.Entity.AttemptId == attemptId && (e.Entity.AlertType == ProctoringAlertType.ExamBlocked || e.Entity.AlertType == ProctoringAlertType.ExamUnblocked)))
            {
                audit.Entity.ActionByUserId = teacherId;
                audit.Entity.ActionByName = actorName.Length > 256 ? actorName[..256] : actorName;
            }
            a.StateVersion++;
            await db.SaveChangesAsync(ct);
            state = State(a, s);
            await tx.CommitAsync(ct);
        });
        if (state is not null) await PublishAsync(state, teacherId, resume ? "ResumeExamSignal" : "ExamLocked");
        return state;
    }

    public async Task NotifySpeechContextAsync(int logId, CancellationToken ct = default)
    {
        var log = await db.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(x => x.LogId == logId, ct);
        if (log?.AttemptId is not int id || log.Severity != ProctoringSeverity.High || !log.CountsAsViolation) return;
        var state = await GetAsync(id, ct);
        if (state is null || state.EventId != logId || state.Status != nameof(AttemptStatus.InProgress)) return;
        var owner = await db.ExamAttempts.Where(x => x.AttemptId == id).Select(x => x.Exam!.CreatedBy).SingleAsync(ct);
        // Enrichment of an existing incident: do not increment the strike or state version.
        if (!string.IsNullOrWhiteSpace(owner))
            await PublishAsync(state, owner, "SpeechContextUpdated");
    }

    private async Task PublishAsync(ProctoringState state, string owner, string name)
    {
        try
        {
            await hub.Clients.Group(ProctoringHub.AttemptGroup(state.AttemptId)).SendAsync(name, state);
            var examId = await db.ExamAttempts.Where(a => a.AttemptId == state.AttemptId).Select(a => a.ExamId).SingleAsync();
            var teachers = await db.ExamInvigilators.Where(i => i.ExamId == examId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null).Select(i => i.TeacherId).ToListAsync();
            teachers.Add(owner);
            var creator = await db.Exams.Where(e => e.ExamId == examId).Select(e => e.CreatedBy).SingleAsync();
            if (creator is not null) teachers.Add(creator);
            await hub.Clients.Groups(teachers.Distinct().Select(ProctoringHub.TeacherGroup).ToArray()).SendAsync("CandidateStateChanged", state);
        }
        catch (Exception ex) { logger.LogWarning(ex, "State {Version} persisted; clients will recover through state synchronization", state.Version); }
    }
}
