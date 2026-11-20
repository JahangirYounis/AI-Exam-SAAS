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
    string? SelectedAnswersJSON, string ResumeUrl, string? EventCode = null, string? CandidateName = null, string BlockingMode = "BOTH", string? Reason = null, string? RiskLevel = null, DateTime? OccurredAt = null, bool HasScreenshot = false, bool HasRecording = false, string? Transcript = null, decimal? SemanticSimilarity = null, int? MatchedQuestionId = null, string ViolationAction = "LockForReview", double? DurationSeconds = null, int AllowedWarnings = 3, string? PolicyVersion = null, int? EventWarningNumber = null, bool EventCountsAsViolation = false, bool AbsenceEnded = false, bool PeopleEnded = false, bool AttentionEnded = false, bool? TabObservationEnded = null, bool? FullscreenObservationEnded = null)
{
    public int WarningCount => Math.Clamp(StrikeCount, 0, AllowedWarnings);
    public int? AbsenceRootId { get; init; }
    public bool? AbsenceActive { get; init; }
}

public interface IProctoringStateService
{
    Task<DeviceHealthReply?> HealthAsync(int attemptId, int sessionId, string studentId, string issue, CancellationToken ct = default);
    Task<DeviceRecoveryReply> RecoverDeviceAsync(int attemptId, int sessionId, string studentId, DeviceReadinessSample sample, CancellationToken ct = default);
    Task<ProctoringState?> GetAsync(int attemptId, CancellationToken ct = default);
    Task ApplyLogAsync(int logId, CancellationToken ct = default);
    Task<bool> CorrectIncidentAsync(int logId, string actor, string reason, CancellationToken ct = default);
    Task<IReadOnlyList<int>> ObserveAsync(int attemptId, int sessionId, string studentId, DateTime sampleAt,
        IReadOnlyList<IncidentObservation> observations, CancellationToken ct = default);
    Task NotifySpeechContextAsync(int logId, CancellationToken ct = default);
    Task NotifyObservationAsync(int logId, CancellationToken ct = default);
    Task<ProctoringState?> ControlAsync(int attemptId, string teacherId, bool resume, string reason, CancellationToken ct = default);
    Task<bool> ResolveAppealAsync(int requestLogId, string teacherId, bool approve, string reason, CancellationToken ct = default);
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
        List<int> enriched = [];
        HashSet<int> absenceUpdated = [];
        HashSet<int> peopleUpdated = [];
        var attentionEnded = false;
        bool? provisionalAbsenceChanged = null;
        HashSet<int> peopleUpgraded = [];
        HashSet<int> peoplePeaks = [];
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            provisionalAbsenceChanged = null;
            attentionEnded = false; ids.Clear(); enriched.Clear(); absenceUpdated.Clear(); peopleUpdated.Clear(); peopleUpgraded.Clear(); peoplePeaks.Clear();
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, attemptId, ct);
            var s = await db.ExamSessionAttempts.Include(x => x.Attempt).ThenInclude(x => x!.Exam).Include(x => x.Session)
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
                // Model 2's own session tracker has already measured multiple
                // independent, quality-checked people for the configured duration.
                // Avoid waiting for the same persistence interval a second time.
                // Uncertain persons, posters and body-only detections do not bypass policy.
                var peopleTemporallySupported = observation.Key == "persons" &&
                    observation.Active == true && observation.PeopleContextSupported &&
                    observation.Log.MaxSimultaneousPeople >= 2 &&
                    observation.MonotonicDurationSeconds is double trackedFor &&
                    double.IsFinite(trackedFor) &&
                    trackedFor >= Math.Max(1, settings.MultiplePersonPersistenceSeconds);
                var supportedAttention = observation.Key == "attention" && observation.Active == true &&
                    (observation.SupportedAttention || observation.RepeatedAttention) &&
                    observation.MonotonicDurationSeconds is double awaySeconds && double.IsFinite(awaySeconds) &&
                    awaySeconds >= Math.Max(2, settings.AttentionPersistenceSeconds) &&
                    awaySeconds <= (now - DateTime.UnixEpoch).TotalSeconds;
                var persistence = observation.Key switch
                {
                    "attention" => supportedAttention ? 0 : Math.Max(2, settings.AttentionPersistenceSeconds),
                    "persons" => peopleTemporallySupported ? 0 : Math.Max(observation.PeopleContextSupported ? 1 : 2, settings.MultiplePersonPersistenceSeconds),
                    // Model 2 already confirmed three seconds of fresh, usable
                    // zero-person frames. Do not wait another three seconds here.
                    "absence" => HasVerifiedPhysicalAbsence(observation,
                        Math.Max(3, settings.FaceAbsencePersistenceSeconds))
                        ? 0 : Math.Max(3, settings.FaceAbsencePersistenceSeconds),
                    "face_visibility" => 3,
                    "objects" => settings.ObjectPersistenceSeconds,
                    "mouth" => .5,
                    "speech" => 0, // already temporally confirmed by VAD within the audio clip
                    _ => throw new ArgumentException("Unsupported incident source.")
                };
                if (!episodes.TryGetValue(observation.Key, out var episode)) episodes[observation.Key] = episode = new();
                // Only observed recovery may re-arm a continuous incident.
                var previousSample = episode.LastSampleUtc;
                var previousActive = episode.LastActive;
                var confirmed = IncidentLifecycle.Observe(episode, observation.Active, now, Math.Clamp(persistence, 0, 60),
                    (observation.Key == "absence" ? 0 : Math.Clamp(settings.IncidentRecoverySeconds, .1, 30)), Math.Clamp(observation.Key is "attention" or "absence" or "persons" or "face_visibility" ? 2 : observation.Key == "objects" ? Math.Max(60, settings.IncidentMaximumSampleGapSeconds) : settings.IncidentMaximumSampleGapSeconds, 2, 120),
                    Math.Clamp(observation.Key is "absence" or "persons" ? 0 : settings.IncidentCooldownSeconds, 0, 120));
                // Sustained, independently supported live presence is actionable even
                // when both people are silent. Uncertain/PAD/background tracks remain review-only.
                var peopleSupported = observation.Key == "persons" && observation.Active == true &&
                    (peopleTemporallySupported ||
                     episode.StartedUtc.HasValue &&
                     now - episode.StartedUtc.Value >= TimeSpan.FromSeconds(Math.Max(1, settings.MultiplePersonPersistenceSeconds))) &&
                    observation.PeopleContextSupported && observation.Log.MaxSimultaneousPeople >= 2;
                if (observation.Key == "persons" && now > previousSample)
                {
                    episode.LastActive = observation.Active;
                    if (!peopleSupported) episode.PeopleVerifiedSinceUtc = null;
                    else if (observation.MonotonicDurationSeconds is double duration && double.IsFinite(duration) && duration >= 0)
                        episode.PeopleVerifiedSinceUtc = now.AddSeconds(-Math.Min(duration, (now - DateTime.UnixEpoch).TotalSeconds));
                    else if (episode.PeopleVerifiedSinceUtc is null || (now - previousSample).TotalSeconds > 2)
                        episode.PeopleVerifiedSinceUtc = now;
                }
                if (observation.Key == "persons" && now > previousSample && episode.LogId is int peopleId)
                {
                    var existingPeople = await db.ProctoringLogs.SingleOrDefaultAsync(l => l.LogId == peopleId && l.AttemptId == attemptId, ct);
                    if (existingPeople is not null && existingPeople.IncidentEndedAtUtc is null)
                    {
                        if (observation.Log.MaxSimultaneousPeople > (existingPeople.MaxSimultaneousPeople ?? 0)) peoplePeaks.Add(existingPeople.LogId);
                        existingPeople.MaxSimultaneousPeople = Math.Max(existingPeople.MaxSimultaneousPeople ?? 0, observation.Log.MaxSimultaneousPeople ?? 0);
                        if (observation.Active == true)
                        {
                            if (previousActive == true && (now - previousSample).TotalSeconds <= 2)
                                existingPeople.ObservedDurationSeconds = (existingPeople.ObservedDurationSeconds ?? 0) + (now - previousSample).TotalSeconds;
                            // Keep the machine-readable snapshot supporting the highest simultaneous count.
                            if (observation.Log.MaxSimultaneousPeople >= existingPeople.MaxSimultaneousPeople)
                                existingPeople.MachineObservationsJson = observation.Log.MachineObservationsJson;
                        }
                        if (observation.Active == false && !episode.Emitted)
                        {
                            peopleUpdated.Add(existingPeople.LogId);
                            existingPeople.IncidentEndedAtUtc = episode.ClearSinceUtc;
                            existingPeople.RecoveryStatus = "AdditionalPeopleCleared";
                        }
                        if (peopleSupported && existingPeople.AlertType == ProctoringAlertType.AdditionalPersonObservation)
                        {
                            existingPeople.AlertType = ProctoringAlertType.MultipleFaces; existingPeople.Severity = ProctoringSeverity.High;
                            existingPeople.PolicyPending = true; existingPeople.PolicyProcessed = false;
                            peopleUpgraded.Add(existingPeople.LogId);
                        }
                        if (existingPeople.AlertType == ProctoringAlertType.MultipleFaces)
                            existingPeople.Description = PeopleDescription(existingPeople.MaxSimultaneousPeople ?? 2);
                        if (peopleSupported && episode.Emitted && !episode.PeopleMinuteWarningSent &&
                            existingPeople.CountsAsViolation && episode.PeopleVerifiedSinceUtc is DateTime verifiedSince &&
                            (now - verifiedSince).TotalSeconds >= 60)
                        {
                            episode.PeopleMinuteWarningSent = true;
                            var warning = new ProctoringLog
                            {
                                AttemptId = attemptId, SessionAttemptId = sessionId, ExamId = a.ExamId,
                                StudentId = studentId, ParentIncidentId = existingPeople.LogId,
                                IncidentKey = "persons:escalation", AlertType = ProctoringAlertType.MultipleFaces,
                                Severity = ProctoringSeverity.High, MaxSimultaneousPeople = observation.Log.MaxSimultaneousPeople,
                                Description = "Multiple persons still detected after 60 seconds.",
                                IncidentStartedAtUtc = existingPeople.IncidentStartedAtUtc,
                                FrameCapturedAt = now, CreatedDate = DateTime.Now,
                                ObservedDurationSeconds = 60, DurationSeconds = 60,
                                PolicyPending = true, ClientEventId = $"persons:{existingPeople.LogId}:60"
                            };
                            db.ProctoringLogs.Add(warning);
                            accepted.Add(warning);
                        }
                    }
                }
                if (observation.Key == "absence" && now > previousSample)
                {
                    episode.LastActive = observation.ProvisionalAbsence ? true : observation.Active;
                    if (!episode.Emitted && !confirmed && (previousActive == true) != (episode.LastActive == true))
                        provisionalAbsenceChanged = episode.LastActive == true;
                    if (observation.Active == true) episode.PotentialReturnUtc = null;
                    if (observation.Active == false) episode.PotentialReturnUtc ??= now;
                    if (episode.LogId is int absenceId)
                    {
                        var ongoingAbsence = await db.ProctoringLogs.SingleOrDefaultAsync(l => l.LogId == absenceId && l.AttemptId == attemptId, ct);
                        if (ongoingAbsence is not null && ongoingAbsence.IncidentEndedAtUtc is null)
                        {
                            var interval = (now - previousSample).TotalSeconds;
                            var supportedDuration = HasVerifiedPhysicalAbsence(observation, 3)
                                ? observation.MonotonicDurationSeconds : null;
                            var previousVerifiedDuration = episode.LastVerifiedAbsenceSeconds;
                            if (supportedDuration.HasValue)
                                episode.LastVerifiedAbsenceSeconds = supportedDuration;
                            if (supportedDuration is double verified)
                            {
                                episode.UsesVerifiedAbsenceClock = true;
                                ongoingAbsence.ObservedDurationSeconds = Math.Max(ongoingAbsence.ObservedDurationSeconds ?? 0, verified);
                            }
                            else if (observation.MonotonicDurationSeconds is null &&
                                !episode.UsesVerifiedAbsenceClock &&
                                previousActive == true && observation.Active.HasValue && interval <= 2)
                                ongoingAbsence.ObservedDurationSeconds = (ongoingAbsence.ObservedDurationSeconds ?? 0) + interval;
                            var previousRecoveryStatus = ongoingAbsence.RecoveryStatus;
                            ongoingAbsence.RecoveryStatus = observation.Active is null ? "TrackingUncertain" : observation.Active == true ? "Absent" : "ReturnPending";
                            if (observation.Active == false && !episode.Emitted)
                            {
                                ongoingAbsence.IncidentEndedAtUtc = episode.PotentialReturnUtc;
                                ongoingAbsence.RecoveryStatus = "ReturnedPhysically";
                            }
                            ongoingAbsence.DurationSeconds = (int)Math.Floor(ongoingAbsence.ObservedDurationSeconds ?? 0);
                            ongoingAbsence.Description = AbsenceDescription(ongoingAbsence.ObservedDurationSeconds ?? 0);
                            if (previousRecoveryStatus != ongoingAbsence.RecoveryStatus)
                                absenceUpdated.Add(ongoingAbsence.LogId);

                            // Milestones require current, continuous model evidence.
                            // Persist the milestone with its child alert under the attempt lock.
                            if (episode.Emitted && supportedDuration is double continuousSeconds &&
                                (!previousVerifiedDuration.HasValue || continuousSeconds >= previousVerifiedDuration.Value))
                            {
                                var milestone = episode.LastAbsenceMilestoneSeconds < 30 ? 30 :
                                    episode.LastAbsenceMilestoneSeconds < 60 ? 60 :
                                    episode.LastAbsenceMilestoneSeconds + 60;
                                // At most one new milestone per fresh observation.
                                if (continuousSeconds >= milestone)
                                {
                                    episode.LastAbsenceMilestoneSeconds = milestone;
                                    var warning = new ProctoringLog
                                    {
                                        AttemptId = attemptId,
                                        SessionAttemptId = sessionId,
                                        ExamId = a.ExamId,
                                        StudentId = studentId,
                                        ParentIncidentId = ongoingAbsence.LogId,
                                        IncidentKey = "absence:escalation",
                                        AlertType = ProctoringAlertType.FaceNotDetected,
                                        Severity = ProctoringSeverity.High,
                                        Description = $"Student still absent — {milestone} seconds",
                                        ModelName = observation.Log.ModelName,
                                        ModelVersion = observation.Log.ModelVersion,
                                        IncidentStartedAtUtc = ongoingAbsence.IncidentStartedAtUtc,
                                        FrameCapturedAt = now,
                                        ObservedDurationSeconds = continuousSeconds,
                                        DurationSeconds = milestone,
                                        PolicyPending = true,
                                        CreatedDate = DateTime.Now,
                                        ClientEventId = $"absence:{ongoingAbsence.LogId}:{milestone}"
                                    };
                                    db.ProctoringLogs.Add(warning);
                                    accepted.Add(warning);
                                }
                            }
                        }
                    }
                    if (confirmed && observation.MonotonicDurationSeconds is double elapsed && (!double.IsFinite(elapsed) || elapsed < 3))
                    {
                        episode.Emitted = false; episode.LastEmittedUtc = null; confirmed = false;
                    }
                }
                if (observation.Key is "attention" or "face_visibility" && now > previousSample && episode.LogId is int priorId)
                {
                    var ongoing = await db.ProctoringLogs.SingleOrDefaultAsync(l => l.LogId == priorId && l.AttemptId == attemptId, ct);
                    if (ongoing?.IncidentStartedAtUtc is DateTime began && ongoing.IncidentEndedAtUtc is null)
                    {
                        if (supportedAttention)
                            ongoing.ObservedDurationSeconds = Math.Max(ongoing.ObservedDurationSeconds ?? 0, observation.MonotonicDurationSeconds!.Value);
                        else if (observation.Active == true && episode.StartedUtc == began)
                            ongoing.ObservedDurationSeconds = Math.Max(ongoing.ObservedDurationSeconds ?? 0, (now - began).TotalSeconds);
                        if (observation.Active == false && !episode.Emitted)
                        {
                            ongoing.IncidentEndedAtUtc = episode.ClearSinceUtc;
                            if (observation.Key == "attention") attentionEnded = true;
                        }
                    }
                }
                if (!confirmed) continue;
                if (observation.Key == "mouth") continue; // Supporting signal only, never a strike.
                if (supportedAttention && !observation.RepeatedAttention)
                    episode.StartedUtc = now.AddSeconds(-observation.MonotonicDurationSeconds!.Value);
                // Correlate only fresh independently accepted candidate speech, never mouth activity alone.
                if (settings.CorrelateAttentionSpeech && observation.Key is "attention" or "speech")
                {
                    var otherKey = observation.Key == "attention" ? "speech" : "attention";
                    if (episodes.TryGetValue(otherKey, out var other) && other.Emitted && other.StartedUtc.HasValue &&
                        other.ClearSinceUtc is null && now >= other.LastSampleUtc && now - other.LastSampleUtc <= TimeSpan.FromSeconds(2))
                    {
                        var prior = await db.ProctoringLogs.Where(l => l.AttemptId == attemptId && l.SessionAttemptId == sessionId && l.IncidentKey == otherKey && l.CountsAsViolation)
                            .OrderByDescending(l => l.LogId).FirstOrDefaultAsync(ct);
                        var speech = observation.Key == "speech" ? observation.Log : prior;
                        if (prior is not null && BlockingPolicy.Qualifies(a.Exam!, observation.Log.AlertType) && speech?.SpeakerAttribution == "mouth_correlated" &&
                            (otherKey != "attention" || now - other.StartedUtc >= TimeSpan.FromSeconds(2)))
                        {
                            var supporting = observation.Log;
                            supporting.StudentId = studentId; supporting.ExamId = a.ExamId;
                            supporting.AttemptId = attemptId; supporting.SessionAttemptId = sessionId;
                            supporting.FrameCapturedAt = sampleAt; supporting.CreatedDate = DateTime.Now;
                            supporting.CountsAsViolation = false; supporting.PolicyProcessed = true; supporting.PolicyPending = false;
                            supporting.ModelVersion = $"Correlated with incident {prior.LogId}";
                            supporting.ClientEventId ??= Guid.NewGuid().ToString("N");
                            db.ProctoringLogs.Add(supporting);
                            supporting.ParentIncidentId ??= prior.LogId;
                            var examConversation = speech.AlertType == ProctoringAlertType.AnswerPromptSpeech;
                            prior.AlertType = examConversation ? ProctoringAlertType.AnswerPromptSpeech : ProctoringAlertType.LookingAwaySpeaking;
                            prior.Severity = ProctoringSeverity.High;
                            prior.Description = examConversation ? "Possible exam-related conversation was detected during the assessment. Review the recorded speech and linked attention observations." : "The student appeared to look away from the exam screen while speaking. This activity may violate exam rules and requires review.";
                            prior.SpeakerAttribution = speech.SpeakerAttribution;
                            prior.SpeechTranscript ??= speech.SpeechTranscript;
                            if (observation.Key == "attention")
                            {
                                prior.IncidentStartedAtUtc = episode.StartedUtc;
                                prior.ObservedDurationSeconds = (now - episode.StartedUtc!.Value).TotalSeconds;
                                episode.LogId = prior.LogId;
                            }
                            enriched.Add(prior.LogId);
                            continue;
                        }
                    }
                }
                var log = observation.Log;
                log.LogId = 0;
                log.StudentId = studentId; log.ExamId = a.ExamId; log.AttemptId = attemptId; log.SessionAttemptId = sessionId;
                log.IncidentKey = observation.Key; log.FrameCapturedAt = sampleAt;
                if (observation.Key is "attention" or "absence" && episode.StartedUtc.HasValue)
                {
                    log.IncidentStartedAtUtc = episode.StartedUtc;
                    log.ObservedDurationSeconds = (now - episode.StartedUtc.Value).TotalSeconds;
                    log.DurationSeconds = (int)Math.Floor((now - episode.StartedUtc.Value).TotalSeconds);
                    if (observation.Key == "attention")
                    {
                        if (supportedAttention)
                        {
                            log.ObservedDurationSeconds = observation.MonotonicDurationSeconds;
                            log.DurationSeconds = (int)Math.Floor(log.ObservedDurationSeconds!.Value);
                        }
                        log.Description = $"{observation.Log.Description} Observed away for {log.ObservedDurationSeconds:0.0} seconds; teacher review required.";
                    }
                }
                if (observation.Key == "absence")
                {
                    log.ClientEventId ??= $"absence-root:{sessionId}:{now.Ticks}";
                    // The camera/model-owned timing starts before the HTTP result.
                    if (observation.MonotonicDurationSeconds is double observedSeconds &&
                        double.IsFinite(observedSeconds) && observedSeconds >= 3 &&
                        observedSeconds <= (now - DateTime.UnixEpoch).TotalSeconds)
                    {
                        log.IncidentStartedAtUtc = now.AddSeconds(-observedSeconds);
                        log.ObservedDurationSeconds = observedSeconds;
                        log.DurationSeconds = (int)Math.Floor(observedSeconds);
                    }
                    episode.LastAbsenceMilestoneSeconds = 0;
                    episode.UsesVerifiedAbsenceClock = HasVerifiedPhysicalAbsence(observation, 3);
                    episode.LastVerifiedAbsenceSeconds = episode.UsesVerifiedAbsenceClock ? observation.MonotonicDurationSeconds : null;
                    log.RecoveryStatus = "Absent";
                    log.Description = AbsenceDescription(log.ObservedDurationSeconds ?? 3);
                }
                log.PolicyPending = true; log.PolicyProcessed = false; log.CountsAsViolation = false;
                if (observation.Key == "face_visibility")
                {
                    log.PolicyPending = false; log.PolicyProcessed = true;
                    log.IncidentStartedAtUtc = episode.StartedUtc;
                    log.ObservedDurationSeconds = (now - episode.StartedUtc!.Value).TotalSeconds;
                    log.ModelName = "FACE_NOT_VISIBLE";
                    log.RecoveryStatus = "FaceNotVisible";
                }
                if (observation.Key == "persons")
                {
                    log.AlertType = peopleSupported ? ProctoringAlertType.MultipleFaces : ProctoringAlertType.AdditionalPersonObservation;
                    log.Severity = peopleSupported ? ProctoringSeverity.High : ProctoringSeverity.Low;
                    log.PolicyPending = peopleSupported; log.PolicyProcessed = !peopleSupported;
                    log.Description = peopleSupported ? PeopleDescription(log.MaxSimultaneousPeople ?? 2) : "An additional person may be visible near the examination area. Participation is uncertain; review the available evidence.";
                    log.IncidentStartedAtUtc = episode.StartedUtc;
                    log.ObservedDurationSeconds = (now - episode.StartedUtc!.Value).TotalSeconds;
                    episode.PeopleMinuteWarningSent = false;
                    if (peopleTemporallySupported)
                        episode.PeopleVerifiedSinceUtc = now.AddSeconds(-Math.Max(1, settings.MultiplePersonPersistenceSeconds));
                    log.RecoveryStatus = "UnderReview";
                }
                log.CreatedDate = DateTime.Now;
                log.ClientEventId ??= Guid.NewGuid().ToString("N");
                db.ProctoringLogs.Add(log); accepted.Add(log);
            }
            s.ProctoringIncidentsJson = JsonSerializer.Serialize(episodes);
            await db.SaveChangesAsync(ct);
            var newRoots = accepted.Where(x => x.IncidentKey is "attention" or "absence" or "persons" or "face_visibility").ToArray();
            if (newRoots.Length > 0)
            {
                foreach (var item in newRoots) episodes[item.IncidentKey!].LogId = item.LogId;
                s.ProctoringIncidentsJson = JsonSerializer.Serialize(episodes);
                await db.SaveChangesAsync(ct);
            }
            ids.AddRange(accepted.Select(x => x.LogId));
            await tx.CommitAsync(ct);
        });
        // Recovery worker also applies pending logs after process interruption.
        foreach (var id in ids) await ApplyLogAsync(id, ct);
        foreach (var id in peopleUpgraded) await ApplyLogAsync(id, ct);
        foreach (var id in enriched) { await NotifySpeechContextAsync(id, ct); if (!ids.Contains(id)) ids.Add(id); }
        if (attentionEnded || peopleUpdated.Count > 0 || peoplePeaks.Count > 0 || (ids.Count > 0 && observations.Any(o => o.Key != "absence")))
        {
            var current = await GetAsync(attemptId, ct);
            var owner = await db.ExamAttempts.Where(a => a.AttemptId == attemptId).Select(a => a.Exam!.CreatedBy).SingleAsync(ct);
            if (current is not null && owner is not null) await PublishAsync(current, owner, "PeopleObservationUpdated");
        }
        foreach (var absenceId in absenceUpdated)
            await NotifyObservationAsync(absenceId, ct);
        if (absenceUpdated.Count == 0 && provisionalAbsenceChanged is bool visible)
        {
            var current = await GetAsync(attemptId, ct);
            if (current is { Status: nameof(AttemptStatus.InProgress) } && current.SessionAttemptId == sessionId)
            {
                var owner = await db.ExamAttempts.Where(a => a.AttemptId == attemptId)
                    .Select(a => a.Exam!.CreatedBy).SingleAsync(ct);
                if (owner is not null)
                    await PublishAsync(current with {
                        EventId = null, AbsenceRootId = null, AbsenceActive = visible,
                        EventCountsAsViolation = false, EventWarningNumber = null, AbsenceEnded = false,
                        ViolationType = nameof(ProctoringAlertType.FaceNotDetected)
                    }, owner, "AbsenceUpdated");
            }
        }
        // When a review-only person incident is upgraded to confirmed, return
        // that same log id for the latest screenshot/video attachment pipeline.
        foreach (var id in peopleUpgraded) if (!ids.Contains(id)) ids.Add(id);
        foreach (var id in peoplePeaks) if (!ids.Contains(id)) ids.Add(id);
        return ids;
    }

    private static bool HasVerifiedPhysicalAbsence(IncidentObservation observation, double requiredSeconds)
    {
        if (observation.Key != "absence" || observation.Active != true ||
            observation.MonotonicDurationSeconds is not double seconds ||
            !double.IsFinite(seconds) || seconds < requiredSeconds ||
            string.IsNullOrWhiteSpace(observation.Log.MachineObservationsJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(observation.Log.MachineObservationsJson);
            var root = doc.RootElement;
            return root.GetProperty("source").GetString() == "Model2Behavior" &&
                root.GetProperty("physicalPresenceState").GetString() == "VERIFIED_PHYSICAL_ABSENCE" &&
                root.GetProperty("personDetectorReady").GetBoolean() &&
                root.GetProperty("cameraPresenceUsable").GetBoolean() &&
                root.GetProperty("modelElapsedSeconds").GetDouble() >= requiredSeconds;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string PeopleDescription(int count) => $"More than one person detected during the test ({count} supported people). Teacher review required; inspect the original camera clip. Detection is not conclusive proof of assistance.";
    private static string AbsenceDescription(double duration) => "Student absent";

    public static bool IsStandard(ProctoringAlertType type) => type is
        ProctoringAlertType.LookingAwaySpeaking or ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit or
        ProctoringAlertType.FaceNotDetected or ProctoringAlertType.MultipleFaces or ProctoringAlertType.HeadAway or
        ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway or
        ProctoringAlertType.ElectronicDeviceDetected or ProctoringAlertType.AnswerPromptSpeech or ProctoringAlertType.TalkingDetected or
        ProctoringAlertType.IdentityMismatch;

    public static bool IsCritical(ProctoringLog log) =>
        log.AlertType == ProctoringAlertType.IdentityMismatch &&
        log.Severity == ProctoringSeverity.High && log.PolicyPending &&
        (log.MachineObservationsJson?.Contains($"\"identityConfirmation\":\"{AiExamProctoring.Services.FaceIdentity.IdentityRecheckPolicy.ConfirmationMarker}\"", StringComparison.Ordinal) == true ||
         log.MachineObservationsJson?.Contains("\"identityConfirmation\":\"three_fresh_batches\"", StringComparison.Ordinal) == true ||
         log.MachineObservationsJson?.Contains("\"identityConfirmation\":\"four_fresh_batches\"", StringComparison.Ordinal) == true);

    public static async Task LockAttemptAsync(ApplicationDbContext context, int id, CancellationToken ct)
    {
        if (context.Database.IsSqlServer())
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT AttemptId FROM ExamAttempts WITH (UPDLOCK, HOLDLOCK) WHERE AttemptId = {id}", ct);
    }

    public static async Task LockEvidenceUploadsAsync(ApplicationDbContext context, int attemptId, CancellationToken ct)
    {
        if (!context.Database.IsSqlServer()) return;
        var resource = $"proctoring-evidence:{attemptId}";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=5000;
            IF @result < 0 THROW 51000, 'Evidence upload queue is busy.', 1;
            """, ct);
    }

    public async Task<ProctoringState?> GetAsync(int attemptId, CancellationToken ct = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            // Read-only UI synchronization must not join the exclusive writer queue.
            // Counter/block/answer mutations retain their authoritative attempt locks.
            // Clients reconcile the persisted StateVersion on subsequent updates.
            var attempt = await db.ExamAttempts.AsNoTracking().Include(a => a.Exam).SingleOrDefaultAsync(a => a.AttemptId == attemptId, ct);
            if (attempt is null) return null;
            var session = await db.ExamSessionAttempts.AsNoTracking().Include(s => s.Session)
                .Where(s => s.AttemptId == attemptId).OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
            var latest = await db.ProctoringLogs.AsNoTracking().Where(l => l.AttemptId == attemptId && (l.CountsAsViolation || attempt.Status == AttemptStatus.ProctoringBlocked && l.AlertType == ProctoringAlertType.ExamBlocked || attempt.Status == AttemptStatus.ProctoringBlocked && l.Description != null && l.Description.StartsWith("[DEVICE_HOLD]")))
                .OrderByDescending(l => l.PolicyAppliedAt ?? l.CreatedDate).ThenByDescending(l => l.LogId).FirstOrDefaultAsync(ct);
            // Recover the triggering alert when the block audit is the latest record.
            // This keeps the original reason and evidence available after reconnect.
            var stateLog = latest;
            if (attempt.Status == AttemptStatus.ProctoringBlocked &&
                latest?.AlertType == ProctoringAlertType.ExamBlocked &&
                latest.ParentIncidentId is int parentId)
            {
                var cause = await db.ProctoringLogs.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LogId == parentId && x.AttemptId == attemptId, ct);
                if (cause is { PolicyProcessed: true } && (cause.CountsAsViolation ||
                    cause.AlertType == ProctoringAlertType.IdentityMismatch && cause.Severity == ProctoringSeverity.High))
                    stateLog = cause;
            }
            var identityHold = attempt.Status == AttemptStatus.ProctoringBlocked &&
                stateLog?.AlertType == ProctoringAlertType.IdentityMismatch &&
                stateLog.Severity == ProctoringSeverity.High;
            var state = State(attempt, session, stateLog) with
            {
                AbsenceRootId = stateLog?.IncidentKey switch { "absence" => stateLog.LogId, "absence:escalation" => stateLog.ParentIncidentId, _ => null },
                EventCode = identityHold ? "IDENTITY_REVIEW_HOLD" : EventCode(stateLog),
                CandidateName = await db.Users.Where(u => u.Id == attempt.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            };
            await tx.CommitAsync(ct);
            if (stateLog?.IncidentKey == "absence:escalation" && stateLog.ParentIncidentId is int absenceRoot)
                state = state with { AbsenceEnded = await db.ProctoringLogs.AnyAsync(l => l.LogId == absenceRoot && l.AttemptId == attemptId && l.IncidentEndedAtUtc != null, ct) };
            if (stateLog?.IncidentKey == "persons:escalation" && stateLog.ParentIncidentId is int peopleRoot)
                state = state with { PeopleEnded = await db.ProctoringLogs.AnyAsync(l => l.LogId == peopleRoot && l.AttemptId == attemptId && l.IncidentEndedAtUtc != null, ct) };
            return state;
        });
    }

    private static bool IsFullscreenObservation(ProctoringLog? log) =>
        log is { AlertType: ProctoringAlertType.FullScreenExit, IncidentKey: "browser" };

    private static DateTime? ObservationTime(ProctoringLog? log) => IsFullscreenObservation(log)
        ? SessionRecordingMetadata.BrowserMarker(log!.MachineObservationsJson)?.UtcDateTime ??
            DateTime.SpecifyKind(log.CreatedDate, DateTimeKind.Utc)
        : log?.FrameCapturedAt ?? log?.CreatedDate;

    private static ProctoringState State(ExamAttempt a, ExamSessionAttempt? s, ProctoringLog? log = null) => new(
        a.AttemptId, s?.SessionAttemptId, a.Status.ToString(), a.StrikeCount, a.StateVersion,
        log?.AlertType.ToString(), log?.LogId, s?.CurrentQuestionIndex ?? 0, s?.LastQuestionId,
        s is null ? 0 : s.FrozenAtUtc.HasValue ? s.RemainingTimeSeconds ?? 0 : Remaining(s),
        s?.SelectedAnswersJSON, $"/Student/Exam/Resume?attemptId={a.AttemptId}", EventCode(log), a.Student?.FullName, (a.Exam?.BlockingMode ?? ExamBlockingMode.BOTH).ToString(), log?.Description, log?.Severity.ToString(), ObservationTime(log), log?.ScreenshotPath is not null, log?.RecordingPath is not null || log?.VideoEvidencePath is not null, null, log?.SemanticSimilarity, log?.MatchedQuestionId, AutomaticViolationAction.LockForReview.ToString(), log?.ObservedDurationSeconds, a.Exam?.AllowedWarnings ?? 3, a.Exam?.BlockingPolicyVersion ?? "legacy-v1", log?.WarningNumber, log?.CountsAsViolation == true, log?.IncidentKey == "absence" && log.IncidentEndedAtUtc.HasValue, log?.IncidentKey == "persons" && log.IncidentEndedAtUtc.HasValue, log?.IncidentKey == "attention" && log.IncidentEndedAtUtc.HasValue,
        FullscreenObservationEnded: IsFullscreenObservation(log) ? log!.IncidentEndedAtUtc.HasValue : null);

    private static string? EventCode(ProctoringLog? log)
    {
        var text = log?.Description;
        if (text?.StartsWith("Protective identity review:") == true) return "IDENTITY_REVIEW_HOLD";
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
        // Preserve earned academic marks at the freeze boundary without creating a
        // terminal result: the existing Teacher resume workflow must remain usable.
        await Services.Results.ExamResultFinalizationService.ScoreSessionAsync(db, s,
            a.Exam?.ShuffleOptions ?? false, null, DateTime.Now, ct: ct);
        a.Status = s.Status = AttemptStatus.ProctoringBlocked;
        db.ProctoringLogs.Add(Audit(a, s, ProctoringAlertType.ExamBlocked, reason));
    }

    private static ProctoringLog Audit(ExamAttempt a, ExamSessionAttempt s, ProctoringAlertType kind, string reason) => new()
    {
        StudentId = a.StudentId,
        ExamId = a.ExamId,
        AttemptId = a.AttemptId,
        SessionAttemptId = s.SessionAttemptId,
        AlertType = kind,
        Description = reason,
        MachineObservationsJson = JsonSerializer.Serialize(new { policyVersion = a.Exam?.BlockingPolicyVersion ?? "legacy-v1", allowedWarnings = a.Exam?.AllowedWarnings ?? 3, strikeCount = a.StrikeCount, resetWarningsOnResume = a.Exam?.ResetWarningsOnResume ?? true }),
        ModelName = "Proctoring policy",
        CountsAsViolation = false,
        PolicyProcessed = true,
        IsReviewed = true,
        ClientEventId = Guid.NewGuid().ToString("N"),
        CreatedDate = DateTime.Now
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
            // No semantic/transcript-only promotion of an already processed screening log.
            // Such evidence remains review-only unless the original independent
            // confirmed-candidate episode is itself marked pending for policy.
            if (log.PolicyProcessed || !log.PolicyPending) return;
            log.PolicyProcessed = true;
            var a = await db.ExamAttempts.Include(a => a.Exam).SingleAsync(a => a.AttemptId == id.Value, ct);
            var s = await db.ExamSessionAttempts.Include(s => s.Session).SingleOrDefaultAsync(s => s.SessionAttemptId == log.SessionAttemptId, ct);
            // Reject mismatched persisted associations before any state or counter mutation.
            if (s?.Session is null || a.Exam is null || s.AttemptId != a.AttemptId ||
                (s.Session.ExamId != 0 && s.Session.ExamId != a.ExamId) ||
                log.StudentId != a.StudentId || log.ExamId != a.ExamId)
            {
                log.CountsAsViolation = false;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            var capturedUtc = log.AlertType == ProctoringAlertType.FullScreenExit && log.IncidentKey == "browser"
                ? SessionRecordingMetadata.BrowserMarker(log.MachineObservationsJson)?.UtcDateTime ??
                    DateTime.SpecifyKind(log.CreatedDate, DateTimeKind.Utc)
                : (log.FrameCapturedAt ?? log.CreatedDate).ToUniversalTime();
            if ((a.TeacherResumedAtUtc is DateTime resumed && capturedUtc <= resumed) ||
                a.CompletedAt.HasValue || a.ResultReleasedAtUtc.HasValue || a.IsDisqualified ||
                s.CompletedAt.HasValue || s.FrozenAtUtc.HasValue)
            {
                log.CountsAsViolation = false;
                log.WarningNumber = null;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            var critical = IsCritical(log);
            if (critical && await db.ProctoringLogs.AnyAsync(x => x.AttemptId == a.AttemptId &&
                x.SessionAttemptId == s.SessionAttemptId &&
                ((x.AlertType == ProctoringAlertType.IdentityVerified || x.ModelName == "Identity monitoring health") &&
                 (x.FrameCapturedAt ?? x.CreatedDate) > (log.FrameCapturedAt ?? log.CreatedDate) ||
                 x.IncidentKey == "absence" && (x.IncidentEndedAtUtc == null || x.IncidentEndedAtUtc >= capturedUtc)), ct))
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            // The attempt row lock serializes decisions from all Python modules,
            // browser events and the policy recovery worker. Only verified categories
            // are countable. A duplicate classification of the same episode is not.
            var nowLocal = DateTime.Now;
            var qualified = BlockingPolicy.ConfirmedQualifying(a.Exam!, log);
            var recent = qualified && !critical
                ? await db.ProctoringLogs.AsNoTracking()
                    .Where(x => x.AttemptId == a.AttemptId && x.CountsAsViolation &&
                                x.PolicyAppliedAt >= nowLocal.AddSeconds(-BlockingPolicy.CorrelatedWindowSeconds))
                    .OrderByDescending(x => x.PolicyAppliedAt).Take(32).ToListAsync(ct)
                : new List<ProctoringLog>();
            var coalesced = recent.Any(previous => BlockingPolicy.IsSameContinuousIncident(previous, log, nowLocal));
            var canCount = !coalesced && qualified;
            if (a.Status == AttemptStatus.InProgress && s.Status == AttemptStatus.InProgress &&
                Remaining(s) > 0 && (critical || canCount))
            {
                // New two-batch identity incidents record one standard warning
                // while retaining the existing protective hold.
                var counted = !critical ||
                    log.MachineObservationsJson?.Contains("\"identityConfirmation\":\"two_fresh_batches\"", StringComparison.Ordinal) == true &&
                    BlockingPolicy.Qualifies(a.Exam!, log.AlertType);
                log.CountsAsViolation = counted;
                if (critical || log.AlertType is ProctoringAlertType.FaceNotDetected or ProctoringAlertType.MultipleFaces
                    or ProctoringAlertType.HeadAway or ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway
                    or ProctoringAlertType.LookingAwaySpeaking or ProctoringAlertType.ElectronicDeviceDetected
                    or ProctoringAlertType.TalkingDetected or ProctoringAlertType.AnswerPromptSpeech)
                    log.Severity = ProctoringSeverity.High;
                if (counted) a.TotalViolations++;
                log.PolicyAppliedAt = nowLocal;
                a.LastViolationAtUtc = DateTime.UtcNow;
                a.StateVersion++;
                if (critical) a.HighSeverityViolations++;
                if (counted) a.StrikeCount = a.StrikeCount == int.MaxValue ? int.MaxValue : a.StrikeCount + 1;
                var allowed = Math.Clamp(a.Exam!.AllowedWarnings, 1, 10);
                log.WarningNumber = counted && a.StrikeCount <= allowed ? a.StrikeCount : null;
                var automaticBlock = !critical && BlockingPolicy.ShouldAutoLock(a.Exam.BlockingMode,
                    a.StrikeCount, false, allowed);
                if (critical || automaticBlock)
                {
                    if (critical && log.IncidentStartedAtUtc.HasValue)
                    {
                        log.IncidentEndedAtUtc = DateTime.UtcNow;
                        log.ObservedDurationSeconds = Math.Max(0, (log.IncidentEndedAtUtc.Value - log.IncidentStartedAtUtc.Value).TotalSeconds);
                        log.RecoveryStatus = "IdentityHold";
                    }
                    await FreezeAsync(a, s, critical
                        ? "Protective identity review: repeated supported mismatches require supervisor review. No misconduct determination."
                        : log.IncidentKey is "absence" or "absence:escalation"
                            ? $"Student blocked — {log.Description}. Teacher review required."
                            : $"Automatic exam block after {a.StrikeCount} qualifying incidents. Teacher review required.", ct);
                }

                log.MachineObservationsJson = JsonSerializer.Serialize(new
                {
                    policyVersion = a.Exam.BlockingPolicyVersion ?? "legacy-v1",
                    allowedWarnings = allowed,
                    violationNumber = counted ? a.StrikeCount : (int?)null,
                    sourceObservations = log.MachineObservationsJson,
                    qualification = critical ? "protective_identity_review" : "verified_policy_incident",
                    incidentId = log.LogId
                });
                foreach (var entry in db.ChangeTracker.Entries<ProctoringLog>()
                    .Where(e => e.State == EntityState.Added && e.Entity.AlertType == ProctoringAlertType.ExamBlocked))
                {
                    entry.Entity.ParentIncidentId = log.LogId;
                    entry.Entity.MachineObservationsJson = JsonSerializer.Serialize(new
                    {
                        policyVersion = a.Exam.BlockingPolicyVersion ?? "legacy-v1",
                        allowedWarnings = allowed,
                        violationCount = a.StrikeCount,
                        triggeringIncidentId = log.LogId,
                        authority = critical ? "identity_protective_hold" : "automatic_policy"
                    });
                    entry.Entity.ActionByName = "Automatic examination policy";
                }
                if (!critical)
                {
                    var title = automaticBlock ? "Examination blocked for review" :
                        a.StrikeCount == allowed && a.Exam.BlockingMode != ExamBlockingMode.MANUAL_ONLY
                            ? "Final examination warning" : $"Examination warning {a.StrikeCount}";
                    var message = automaticBlock
                        ? $"A qualifying incident ({log.AlertType}) triggered an automatic pause. Your saved answers and remaining time are preserved. Teacher review is required. Incident #{log.LogId}."
                        : $"Incident #{log.LogId}: {log.AlertType}. {(a.StrikeCount == allowed && a.Exam.BlockingMode != ExamBlockingMode.MANUAL_ONLY ? "This is your final warning." : "The Teacher has been informed.")}";
                    db.StudentNotifications.Add(new StudentNotification
                    {
                        StudentId = a.StudentId,
                        ExamId = a.ExamId,
                        Title = title,
                        Message = message,
                        Type = automaticBlock ? "ProctoringBlocked" : "ProctoringWarning",
                        CreatedDate = nowLocal,
                        IsRead = false
                    });
                }
                owner = a.Exam.CreatedBy;
                state = State(a, s, log) with
                {
                    AbsenceRootId = log.IncidentKey switch { "absence" => log.LogId, "absence:escalation" => log.ParentIncidentId, _ => null },
                    EventCode = critical && a.Status == AttemptStatus.ProctoringBlocked ? "IDENTITY_REVIEW_HOLD" : EventCode(log),
                    CandidateName = await db.Users.Where(u => u.Id == a.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
                };
            }
            else
            {
                // Persist a review-only record and notify the correct live room.
                // No strike, no automatic block; teacher may inspect original media.
                log.CountsAsViolation = false;
                log.WarningNumber = null;
                log.PolicyAppliedAt = nowLocal;
                if (a.Status == AttemptStatus.InProgress && s.Status == AttemptStatus.InProgress)
                {
                    a.StateVersion++;
                    owner = a.Exam!.CreatedBy;
                    state = State(a, s, log) with
                    {
                        CandidateName = await db.Users.Where(u => u.Id == a.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
                    };
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        if (state is not null && owner is not null)
            await PublishAsync(state, owner, state.Status == nameof(AttemptStatus.ProctoringBlocked) ? "ExamLocked" :
                state.ViolationType is not null && state.EventId == logId &&
                await db.ProctoringLogs.AsNoTracking().AnyAsync(x => x.LogId == logId && x.CountsAsViolation, ct)
                    ? "StrikeIssued" : "ReviewObservation");
    }

    public async Task<ProctoringState?> ControlAsync(int attemptId, string teacherId, bool resume, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 300) return null;
        var adminResume = await (from membership in db.UserRoles
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
            var a = await db.ExamAttempts.Include(a => a.Exam).SingleOrDefaultAsync(a => a.AttemptId == attemptId && (adminResume || a.Exam!.CreatedBy == teacherId || db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == teacherId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null && i.RevokedAtUtc == null)), ct);
            if (a is null || (!resume && !BlockingPolicy.CanManuallyBlock(a.Exam!.BlockingMode)) || a.Status != (resume ? AttemptStatus.ProctoringBlocked : AttemptStatus.InProgress)) return;
            if (resume && (a!.CompletedAt.HasValue || a.ResultReleasedAtUtc.HasValue || a.IsDisqualified ||
                a.Exam!.Status is ExamStatus.Completed or ExamStatus.Cancelled ||
                await db.ExamResults.AnyAsync(r => r.AttemptId == attemptId, ct))) return;
            // A pending student appeal must be resolved through the audited request endpoint.
            // Legacy one-click unblocks cannot bypass that request or its decision history.
            var currentLockId = resume ? await db.ProctoringLogs.AsNoTracking()
                .Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
                .OrderByDescending(l => l.LogId).Select(l => l.LogId).FirstOrDefaultAsync(ct) : 0;
            if (resume && await db.ProctoringLogs.AnyAsync(req => req.AttemptId == attemptId &&
                req.AlertType == ProctoringAlertType.ReviewRequested &&
                req.ModelName == ReviewRequestRules.RequestModel && !req.IsReviewed &&
                (currentLockId > 0 ? req.LogId > currentLockId && req.ModelVersion != null &&
                    req.ModelVersion.StartsWith("lock:") : req.ModelVersion == "lock:" + a!.StateVersion), ct)) return;
            if (resume && await db.ProctoringLogs.AnyAsync(d => d.AttemptId == attemptId &&
                d.ModelName == ReviewRequestRules.DecisionModel && d.ModelVersion == "rejected" &&
                d.ParentIncidentId.HasValue && db.ProctoringLogs.Any(r => r.LogId == d.ParentIncidentId &&
                    r.ModelName == ReviewRequestRules.RequestModel &&
                    (currentLockId > 0 ? r.LogId > currentLockId && r.ModelVersion != null &&
                        r.ModelVersion.StartsWith("lock:") : r.ModelVersion == "lock:" + a!.StateVersion)), ct)) return;
            var s = await db.ExamSessionAttempts.Include(s => s.Session).Where(s => s.AttemptId == attemptId && s.Status == a.Status)
                .OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
            if (s is null) return;
            if (resume)
            {
                // A Teacher cannot bypass device recovery by pressing the generic
                // cheating-block Resume button. Technical holds have their own
                // fresh-readiness endpoint and preserve the existing strike count.
                if (s.DeviceHealthJson is not null)
                {
                    try
                    {
                        var health = JsonSerializer.Deserialize<DeviceHealthEpisode>(s.DeviceHealthJson);
                        if (health?.IsTechnicalHold == true) return;
                    }
                    catch (JsonException) { return; }
                }
                if (s.Session is null) return;
                int preservedSeconds;
                if (s.FrozenAtUtc.HasValue)
                {
                    preservedSeconds = Math.Max(0, s.RemainingTimeSeconds ?? 0);
                    if (preservedSeconds == 0) return; // No remaining time: teacher must review lifecycle.
                    // Move the clock origin so the ACTIVE session expires after exactly
                    // the preserved seconds, regardless of the length of this hold.
                    s.StartedAt = DateTime.Now.AddSeconds(preservedSeconds).AddMinutes(-s.Session.DurationMinutes);
                }
                else // Compatibility with older blocked attempts that lacked FrozenAtUtc.
                {
                    var blockedAt = await db.ProctoringLogs.Where(l => l.AttemptId == attemptId && l.AlertType == ProctoringAlertType.ExamBlocked)
                        .OrderByDescending(l => l.LogId).Select(l => (DateTime?)l.CreatedDate).FirstOrDefaultAsync(ct);
                    if (blockedAt.HasValue && s.StartedAt.HasValue) s.StartedAt += DateTime.Now - blockedAt.Value;
                    preservedSeconds = Remaining(s);
                    if (preservedSeconds == 0) return;
                }
                // Crucial: a resumed InProgress session MUST NOT remain frozen.
                // ExamLifecycleService deliberately excludes FrozenAtUtc != null.
                s.FrozenAtUtc = null;
                s.RemainingTimeSeconds = null;
                s.ProctoringIncidentsJson = null;
                s.DeviceHealthJson = null;
                s.ObjectTrackingJson = null;
                a.Status = s.Status = AttemptStatus.InProgress;
                if (a.Exam!.ResetWarningsOnResume) a.StrikeCount = 0;
                a.LastViolationAtUtc = null;
                a.TeacherResumedAtUtc = DateTime.UtcNow;
                db.ProctoringLogs.Add(Audit(a, s, ProctoringAlertType.ExamUnblocked, $"{(adminResume ? "Administrator" : "Teacher")} {teacherId} authorized resume. {reason}"));
                db.StudentNotifications.Add(new StudentNotification { StudentId = a.StudentId, ExamId = a.ExamId, Title = "Your teacher has unblocked your exam", Message = "You may resume your exam from the saved question and remaining time. Open your examinations and choose Resume. The entry window does not prevent this approved return.", Type = "ExamResumed" });
            }
            else
            {
                await FreezeAsync(a, s, $"Teacher {teacherId}: {reason}", ct);
                db.StudentNotifications.Add(new StudentNotification
                {
                    StudentId = a.StudentId,
                    ExamId = a.ExamId,
                    Title = "Examination paused by Teacher",
                    Message = "Your Teacher paused the examination for review. Your saved answers and remaining time are preserved. Reason: " + reason,
                    Type = "TeacherReviewHold",
                    CreatedDate = DateTime.Now,
                    IsRead = false
                });
            }
            var actorName = await db.Users.Where(u => u.Id == teacherId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? teacherId;
            foreach (var audit in db.ChangeTracker.Entries<ProctoringLog>().Where(e => e.State == EntityState.Added && e.Entity.AttemptId == attemptId && (e.Entity.AlertType == ProctoringAlertType.ExamBlocked || e.Entity.AlertType == ProctoringAlertType.ExamUnblocked)))
            {
                audit.Entity.ActionByUserId = teacherId;
                audit.Entity.ActionByName = actorName.Length > 256 ? actorName[..256] : actorName;
            }
            a.StateVersion++;
            var controlAudit = db.ChangeTracker.Entries<ProctoringLog>().Where(e => e.State == EntityState.Added && e.Entity.AttemptId == attemptId && (e.Entity.AlertType == ProctoringAlertType.ExamBlocked || e.Entity.AlertType == ProctoringAlertType.ExamUnblocked)).Select(e => e.Entity).LastOrDefault();
            await db.SaveChangesAsync(ct);
            state = State(a, s, controlAudit);
            await tx.CommitAsync(ct);
        });
        if (state is not null) await PublishAsync(state, teacherId, resume ? "ResumeExamSignal" : "ExamLocked");
        return state;
    }


    /// <summary>
    /// Approve/reject exactly one persisted student appeal. The attempt's SQL row lock
    /// serializes concurrent teachers; state change, decision audit and student notice
    /// commit together. No answers, marks or evidence are removed.
    /// </summary>
    public async Task<bool> ResolveAppealAsync(int requestLogId, string teacherId, bool approve,
        string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacherId) || !ReviewRequestRules.IsValidReason(reason)) return false;
        ProctoringState? resumeState = null;
        string? examOwner = null;
        bool committed = false;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            committed = false;
            resumeState = null;
            examOwner = null;
            db.ChangeTracker.Clear();
            // Find a candidate attempt before taking the lock; re-authorize inside it.
            var target = await db.ProctoringLogs.AsNoTracking().Where(l => l.LogId == requestLogId &&
                l.ModelName == ReviewRequestRules.RequestModel && l.AttemptId.HasValue)
                .Select(l => l.AttemptId!.Value).SingleOrDefaultAsync(ct);
            if (target == 0) return;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await LockAttemptAsync(db, target, ct);
            var request = await db.ProctoringLogs.SingleOrDefaultAsync(l => l.LogId == requestLogId &&
                l.ModelName == ReviewRequestRules.RequestModel && l.AlertType == ProctoringAlertType.ReviewRequested &&
                l.AttemptId == target, ct);
            var attempt = await db.ExamAttempts.Include(a => a.Exam).Include(a => a.Student)
                .SingleOrDefaultAsync(a => a.AttemptId == target &&
                    db.UserRoles.Any(ur => ur.UserId == teacherId &&
                        db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Teacher")) &&
                    (a.Exam!.CreatedBy == teacherId ||
                    db.ExamInvigilators.Any(i => i.ExamId == a.ExamId && i.TeacherId == teacherId &&
                        i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null && i.RevokedAtUtc == null)) &&
                    db.UserRoles.Any(ur => ur.UserId == a.StudentId &&
                        db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Student")), ct);
            var lastBlockId = await db.ProctoringLogs.AsNoTracking()
                .Where(l => l.AttemptId == target && l.AlertType == ProctoringAlertType.ExamBlocked)
                .OrderByDescending(l => l.LogId).Select(l => l.LogId).FirstOrDefaultAsync(ct);
            if (request is null || attempt is null || request.ExamId != attempt.ExamId ||
                request.StudentId != attempt.StudentId ||
                !ReviewRequestRules.BelongsToCurrentLock(request.LogId, lastBlockId,
                    request.ModelVersion, attempt.StateVersion) ||
                request.IsReviewed || !ReviewRequestRules.CanResolve(attempt,
                    await db.ExamResults.AnyAsync(r => r.AttemptId == target, ct)) ||
                await db.ProctoringLogs.AnyAsync(l => l.ParentIncidentId == requestLogId &&
                    l.ModelName == ReviewRequestRules.DecisionModel, ct)) return;

            ExamSessionAttempt? session = null;
            ProctoringLog? approvalAudit = null;
            if (approve)
            {
                session = await db.ExamSessionAttempts.Include(s => s.Session)
                    .Where(s => s.AttemptId == target && s.Status == AttemptStatus.ProctoringBlocked)
                    .OrderByDescending(s => s.SessionAttemptId).FirstOrDefaultAsync(ct);
                if (session?.Session is null || !session.Session.IsActive || session.CompletedAt.HasValue)
                    return;
                // A technical device hold needs fresh browser/device recovery, not teacher override.
                if (session.DeviceHealthJson is not null)
                {
                    try
                    {
                        var health = JsonSerializer.Deserialize<DeviceHealthEpisode>(session.DeviceHealthJson);
                        if (health?.IsTechnicalHold == true) return;
                    }
                    catch (JsonException) { return; }
                }
                int remaining;
                if (session.FrozenAtUtc.HasValue)
                {
                    remaining = Math.Max(0, session.RemainingTimeSeconds ?? 0);
                    if (remaining == 0) return;
                    session.StartedAt = DateTime.Now.AddSeconds(remaining)
                        .AddMinutes(-session.Session.DurationMinutes);
                }
                else
                {
                    var blockedAt = await db.ProctoringLogs.Where(l => l.AttemptId == target &&
                        l.AlertType == ProctoringAlertType.ExamBlocked).OrderByDescending(l => l.LogId)
                        .Select(l => (DateTime?)l.CreatedDate).FirstOrDefaultAsync(ct);
                    if (blockedAt.HasValue && session.StartedAt.HasValue)
                        session.StartedAt += DateTime.Now - blockedAt.Value;
                    remaining = Remaining(session);
                    if (remaining <= 0) return;
                }
                session.FrozenAtUtc = null;
                session.RemainingTimeSeconds = null;
                session.ProctoringIncidentsJson = null;
                session.DeviceHealthJson = null;
                session.ObjectTrackingJson = null;
                attempt.Status = session.Status = AttemptStatus.InProgress;
                if (attempt.Exam!.ResetWarningsOnResume) attempt.StrikeCount = 0;
                attempt.LastViolationAtUtc = null;
                attempt.TeacherResumedAtUtc = DateTime.UtcNow;
                attempt.StateVersion++;
                var unblockedAudit = Audit(attempt, session, ProctoringAlertType.ExamUnblocked,
                    $"Teacher {teacherId} approved review request #{requestLogId}. {reason.Trim()}");
                unblockedAudit.ActionByUserId = teacherId;
                unblockedAudit.ActionByName = await db.Users.Where(u => u.Id == teacherId)
                    .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? teacherId;
                db.ProctoringLogs.Add(unblockedAudit);
                approvalAudit = unblockedAudit;
            }

            request.IsReviewed = true;
            var reviewerName = await db.Users.Where(u => u.Id == teacherId)
                .Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? teacherId;
            db.ProctoringLogs.Add(new ProctoringLog
            {
                StudentId = attempt.StudentId, ExamId = attempt.ExamId, AttemptId = target,
                ParentIncidentId = requestLogId, AlertType = ProctoringAlertType.ReviewRequested,
                ClientEventId = Guid.NewGuid().ToString("N"),
                ModelName = ReviewRequestRules.DecisionModel,
                ModelVersion = approve ? "approved" : "rejected", CountsAsViolation = false,
                PolicyProcessed = true, IsReviewed = true,
                ActionByUserId = teacherId,
                ActionByName = reviewerName.Length > 256 ? reviewerName[..256] : reviewerName,
                CreatedDate = DateTime.Now,
                Description = $"{(approve ? "Approved for resumption" : "Block confirmed")}: {reason.Trim()}",
                MachineObservationsJson = JsonSerializer.Serialize(new
                {
                    requestLogId, decision = approve ? "approved" : "rejected", reviewerId = teacherId,
                    originalLockVersion = request.ModelVersion, reviewedAtUtc = DateTime.UtcNow
                })
            });
            db.StudentNotifications.Add(new StudentNotification
            {
                StudentId = attempt.StudentId, ExamId = attempt.ExamId,
                Type = "ReviewRequestResolved",
                Title = approve ? "Your review request was approved" : "Your examination block was confirmed",
                Message = approve
                    ? "Your teacher reviewed the request and authorized resumption. Reopen your examination to continue from saved answers."
                    : "Your teacher reviewed the evidence and confirmed the block. Your saved answers and recorded marks remain preserved. Reason: " + reason.Trim(),
                CreatedDate = DateTime.Now, IsRead = false
            });
            await db.SaveChangesAsync(ct);
            if (approvalAudit is not null) resumeState = State(attempt, session, approvalAudit);
            await transaction.CommitAsync(ct);
            examOwner = attempt.Exam?.CreatedBy;
            committed = true;
        });
        if (committed && approve && resumeState is not null)
            await PublishAsync(resumeState, examOwner ?? teacherId, "ResumeExamSignal");
        return committed;
    }

    public async Task NotifyObservationAsync(int logId, CancellationToken ct = default)
    {
        var log = await db.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(l => l.LogId == logId, ct);
        if (log?.AttemptId is not int attempt) return;
        var state = await GetAsync(attempt, ct);
        var owner = await db.ExamAttempts.Where(a => a.AttemptId == attempt).Select(a => a.Exam!.CreatedBy).SingleAsync(ct);
        var tabObservation = log.AlertType is ProctoringAlertType.ExamPageHidden or ProctoringAlertType.TabSwitch;
        var fullscreenObservation = IsFullscreenObservation(log);
        var absenceObservation = log.IncidentKey == "absence";
        if ((tabObservation || fullscreenObservation || absenceObservation) && log.SessionAttemptId != state?.SessionAttemptId) return;
        var captureTime = tabObservation ? SessionRecordingMetadata.BrowserMarker(log.MachineObservationsJson) : null;
        if (state is not null && owner is not null) await PublishAsync(state with {
            EventId = log.LogId, EventWarningNumber = log.WarningNumber, EventCountsAsViolation = log.CountsAsViolation,
            AbsenceRootId = absenceObservation ? log.LogId : null,
            AbsenceEnded = absenceObservation && log.IncidentEndedAtUtc.HasValue,
            AbsenceActive = absenceObservation ? log.RecoveryStatus == "Absent" : null,
            ViolationType = log.AlertType.ToString(), Reason = log.Description,
            TabObservationEnded = tabObservation ? log.IncidentEndedAtUtc.HasValue : null,
            FullscreenObservationEnded = fullscreenObservation ? log.IncidentEndedAtUtc.HasValue : null,
            OccurredAt = tabObservation ? captureTime?.UtcDateTime ?? DateTime.SpecifyKind(log.CreatedDate, DateTimeKind.Utc)
                : ObservationTime(log),
            HasScreenshot = log.ScreenshotPath is not null,
            HasRecording = log.RecordingPath is not null || log.VideoEvidencePath is not null,
            Transcript = log.SpeechTranscript, MatchedQuestionId = log.MatchedQuestionId
        }, owner, absenceObservation ? "AbsenceUpdated" : "BrowserObservationUpdated");
    }

    public async Task NotifySpeechContextAsync(int logId, CancellationToken ct = default)
    {
        var log = await db.ProctoringLogs.AsNoTracking().SingleOrDefaultAsync(x => x.LogId == logId, ct);
        if (log?.AttemptId is not int id || log.AlertType != ProctoringAlertType.AnswerPromptSpeech) return;
        var state = await GetAsync(id, ct);
        if (state is null || state.Status != nameof(AttemptStatus.InProgress)) return;
        var owner = await db.ExamAttempts.Where(x => x.AttemptId == id).Select(x => x.Exam!.CreatedBy).SingleAsync(ct);
        // A semantic review needs a Teacher notification even when the speaker is
        // unknown and the event did not (and must not) add a strike.
        if (!string.IsNullOrWhiteSpace(owner))
            await PublishAsync(state with
            {
                EventId = log.LogId, EventWarningNumber = log.WarningNumber, EventCountsAsViolation = log.CountsAsViolation, AbsenceEnded = false,
                ViolationType = log.AlertType.ToString(),
                Reason = log.Description,
                OccurredAt = log.FrameCapturedAt ?? log.CreatedDate,
                HasScreenshot = log.ScreenshotPath is not null,
                HasRecording = log.RecordingPath is not null || log.VideoEvidencePath is not null,
                Transcript = log.SpeechTranscript,
                MatchedQuestionId = log.MatchedQuestionId
            }, owner, "SpeechContextUpdated");
    }

    private async Task PublishAsync(ProctoringState state, string owner, string name)
    {
        state = state with { Transcript = null }; // Raw transcripts belong only in authorized evidence review.
        try
        {
            await hub.Clients.Group(ProctoringHub.AttemptGroup(state.AttemptId)).SendAsync(name, state);
            var examId = await db.ExamAttempts.Where(a => a.AttemptId == state.AttemptId).Select(a => a.ExamId).SingleAsync();
            var teachers = await db.ExamInvigilators.Where(i => i.ExamId == examId && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null && i.RevokedAtUtc == null).Select(i => i.TeacherId).ToListAsync();
            teachers.Add(owner);
            var creator = await db.Exams.Where(e => e.ExamId == examId).Select(e => e.CreatedBy).SingleAsync();
            if (creator is not null) teachers.Add(creator);
            await hub.Clients.Groups(teachers.Distinct().Select(ProctoringHub.TeacherGroup).ToArray()).SendAsync("CandidateStateChanged", state);
        }
        catch (Exception ex) { logger.LogWarning(ex, "State {Version} persisted; clients will recover through state synchronization", state.Version); }
    }
}
