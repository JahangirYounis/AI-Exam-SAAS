using AiExamProctoring.Models;
using System.Text.Json;
using ExamEntity = global::AiExamProctoring.Models.Exam;
namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Server-side counting policy.  Inference observations are not proof of misconduct.
/// Callers must serialize updates for the *attempt*, and count a log only once.
/// </summary>
public static class BlockingPolicy
{
    public const int CorrelatedWindowSeconds = 10;

    public static bool ShouldAutoLock(ExamBlockingMode mode, int strikes, bool confirmedIdentityMismatch,
        int allowedWarnings = 3) =>
        (mode is ExamBlockingMode.AUTO_ONLY or ExamBlockingMode.BOTH) &&
        (confirmedIdentityMismatch || strikes > Math.Clamp(allowedWarnings, 1, 10));

    // Automatic mode still permits an authorized teacher to block/resume manually.
    public static bool CanManuallyBlock(ExamBlockingMode mode) => Enum.IsDefined(mode);

    // Used to populate the teacher's policy checklist (category selection only).
    public static bool Qualifies(ExamEntity exam, ProctoringAlertType type)
    {
        if (!ProctoringStateService.IsStandard(type)) return false;
        if (string.IsNullOrWhiteSpace(exam.QualifyingAlertsJson)) return true;
        try
        {
            var classes = JsonSerializer.Deserialize<List<string>>(exam.QualifyingAlertsJson);
            return classes?.Contains(type.ToString(), StringComparer.Ordinal) == true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Additional mandatory qualification. This is evaluated inside the SQL transaction
    /// before any strike changes. Semantic question similarity, sound energy, unknown
    /// speaker, low-quality tracking and technical events NEVER qualify by themselves.
    /// </summary>
    public static bool ConfirmedQualifying(ExamEntity exam, ProctoringLog log)
    {
        if (!log.PolicyPending) return false;
        if (log.AlertType is ProctoringAlertType.TalkingDetected or ProctoringAlertType.AnswerPromptSpeech)
            return ConfirmedSpeech(exam, log);
        if (!Qualifies(exam, log.AlertType) || log.AlertType == ProctoringAlertType.IdentityMismatch) return false;

        return log.AlertType switch
        {
            ProctoringAlertType.LookingAwaySpeaking =>
                log.IncidentKey == "attention" && (log.ObservedDurationSeconds ?? log.DurationSeconds ?? 0) >= 2,
            ProctoringAlertType.FaceNotDetected =>
                (log.IncidentKey == "absence" && (log.ObservedDurationSeconds ?? log.DurationSeconds ?? 0) >= 3) ||
                (IsAbsenceEscalation(log) && log.ObservedDurationSeconds >= 30 - .000001),
            ProctoringAlertType.MultipleFaces =>
                (log.IncidentKey == "persons" || IsPeopleEscalation(log) && log.ObservedDurationSeconds >= 60) && log.MaxSimultaneousPeople >= 2 &&
                log.Severity == ProctoringSeverity.High,
            ProctoringAlertType.ElectronicDeviceDetected =>
                log.IncidentKey?.StartsWith("object:", StringComparison.Ordinal) == true &&
                log.Severity == ProctoringSeverity.High &&
                (log.ObservedDurationSeconds ?? 0) >= ProctoringObjectPolicy.MinimumConfirmationSeconds,
            ProctoringAlertType.HeadAway or ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway =>
                log.IncidentKey == "attention" && (log.ObservedDurationSeconds ?? log.DurationSeconds ?? 0) >= 2,
            ProctoringAlertType.TabSwitch =>
                (log.ModelName == "Browser Session Controls" && !string.IsNullOrWhiteSpace(log.ClientEventId)) ||
                (log.ModelName == "Browser visibility signals" && log.IncidentKey == "browser" &&
                 log.ObservedDurationSeconds >= 0.6 && !string.IsNullOrWhiteSpace(log.ClientEventId)),
            ProctoringAlertType.FullScreenExit =>
                exam.RequireFullscreen && log.IncidentKey == "browser" && log.ModelName == "Browser visibility signals" &&
                log.ObservedDurationSeconds >= 1.5 && !string.IsNullOrWhiteSpace(log.ClientEventId),
            _ => false
        };
    }

    internal static bool ConfirmedSpeech(ExamEntity exam, ProctoringLog log)
    {
        if (log.IncidentKey != "speech" || log.ModelName != "Speech screening" ||
            log.ParentIncidentId.HasValue || string.IsNullOrWhiteSpace(log.SpeechTranscript))
            return false;
        try
        {
            using var observations = JsonDocument.Parse(log.MachineObservationsJson ?? "{}");
            var evidence = observations.RootElement;
            if (evidence.ValueKind != JsonValueKind.Object) return false;
            bool Verified(string field) => evidence.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.True;
            // Content confirmation is independent of speaker attribution. Both paths
            // require server-owned capture context and an intelligible transcript.
            if (!Verified("captureContextReliable") ||
                !evidence.TryGetProperty("transcriptionStatus", out var status) || status.GetString() != "available")
                return false;
            return (log.AlertType == ProctoringAlertType.AnswerPromptSpeech && log.MatchedQuestionId.HasValue &&
                    Verified("questionSpeechConfirmed") && Qualifies(exam, ProctoringAlertType.AnswerPromptSpeech)) ||
                (log.AlertType == ProctoringAlertType.TalkingDetected && Verified("unrelatedSpeechConfirmed") &&
                    evidence.TryGetProperty("unrelatedSpeechEpisode", out var ordinal) &&
                    ordinal.TryGetInt32(out var number) && number >= 3 &&
                    Qualifies(exam, ProctoringAlertType.TalkingDetected));
        }
        catch (JsonException) { return false; }
    }

    private static bool IsAbsenceEscalation(ProctoringLog log) =>
        log.IncidentKey == "absence:escalation" && log.ParentIncidentId.HasValue;

    /// <summary>
    /// Coalesce duplicate inference views of *one* continuous incident. Different
    /// independent categories can each count, even if sampled concurrently. Object
    /// track IDs may change due to camera jitter, so identical object labels are
    /// coalesced for the short interval too.
    /// </summary>
    private static bool IsPeopleEscalation(ProctoringLog log) =>
        log.IncidentKey == "persons:escalation" && log.ParentIncidentId.HasValue;

    public static bool IsSameContinuousIncident(ProctoringLog previous, ProctoringLog incoming,
        DateTime appliedAtLocal, int seconds = CorrelatedWindowSeconds)
    {
        if (previous.SessionAttemptId != incoming.SessionAttemptId) return false;
        // Distinct canonical speech episodes are separated by observed quiet.
        // The generic ten-second correlation window must not swallow a new episode.
        if (previous.IncidentKey == "speech" && incoming.IncidentKey == "speech" &&
            previous.ModelName == "Speech screening" && incoming.ModelName == "Speech screening")
            return previous.LogId == incoming.LogId ||
                previous.ModelVersion is not null && previous.ModelVersion == incoming.ModelVersion;
        if (IsAbsenceEscalation(previous) || IsAbsenceEscalation(incoming) || IsPeopleEscalation(previous) || IsPeopleEscalation(incoming))
            return previous.ClientEventId is not null && previous.ClientEventId == incoming.ClientEventId;

        var previousAt = previous.PolicyAppliedAt ?? previous.CreatedDate;
        if (Math.Abs((appliedAtLocal - previousAt).TotalSeconds) > seconds) return false;
        if (previous.ParentIncidentId == incoming.LogId || incoming.ParentIncidentId == previous.LogId) return true;
        if (previous.ParentIncidentId.HasValue && previous.ParentIncidentId == incoming.ParentIncidentId) return true;
        if (previous.IncidentKey?.StartsWith("object:", StringComparison.Ordinal) == true &&
            incoming.IncidentKey?.StartsWith("object:", StringComparison.Ordinal) == true)
            return previous.IncidentKey == incoming.IncidentKey;
        if (previous.AlertType == ProctoringAlertType.FullScreenExit && incoming.AlertType == ProctoringAlertType.FullScreenExit &&
            previous.IncidentKey == "browser" && incoming.IncidentKey == "browser")
            return previous.ClientEventId == incoming.ClientEventId || previous.IncidentEndedAtUtc == null ||
                (SessionRecordingMetadata.BrowserReturn(previous.MachineObservationsJson, previous.IncidentEndedAtUtc)?.UtcDateTime
                    ?? previous.IncidentEndedAtUtc.Value) > incoming.IncidentStartedAtUtc;
        if (previous.IncidentEndedAtUtc.HasValue && incoming.IncidentStartedAtUtc.HasValue &&
            previous.IncidentEndedAtUtc.Value < incoming.IncidentStartedAtUtc.Value) return false;
        if (previous.IncidentKey is not null && previous.IncidentKey == incoming.IncidentKey) return true;

        static string Family(ProctoringLog x) => x.AlertType switch
        {
            ProctoringAlertType.HeadAway or ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway => "attention",
            ProctoringAlertType.TabSwitch or ProctoringAlertType.FullScreenExit => "browser-navigation",
            ProctoringAlertType.TalkingDetected or ProctoringAlertType.AnswerPromptSpeech => "speech",
            ProctoringAlertType.LookingAwaySpeaking => "attention-speech",
            ProctoringAlertType.ElectronicDeviceDetected => "object:" + (x.ModelName ?? "unknown"),
            _ => x.AlertType.ToString()
        };
        var first = Family(previous);
        var second = Family(incoming);
        return first == second ||
            first == "attention-speech" && second is "attention" or "speech" ||
            second == "attention-speech" && first is "attention" or "speech";
    }
}

