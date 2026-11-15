using AiExamProctoring.Models;
namespace AiExamProctoring.Services.Proctoring;

// V1 durable inbox is a projection of committed incident logs, not a second event pipeline.
public static class TeacherAlertContract
{
    public static readonly ProctoringAlertType[] MonitoringKinds =
    [
        ProctoringAlertType.FaceNotDetected, ProctoringAlertType.HeadAway, ProctoringAlertType.GazeAway,
        ProctoringAlertType.LookAway, ProctoringAlertType.LookingAwaySpeaking, ProctoringAlertType.IdentityMismatch,
        ProctoringAlertType.MultipleFaces, ProctoringAlertType.AdditionalPersonObservation,
        ProctoringAlertType.AudioDetected, ProctoringAlertType.TalkingDetected, ProctoringAlertType.AnswerPromptSpeech,
        ProctoringAlertType.ElectronicDeviceDetected, ProctoringAlertType.ObjectObservation,
        ProctoringAlertType.TabSwitch, ProctoringAlertType.ExamPageHidden, ProctoringAlertType.FullScreenExit,
        ProctoringAlertType.CameraAccessLost, ProctoringAlertType.MicrophoneAccessLost,
        ProctoringAlertType.CameraMonitoringInterrupted, ProctoringAlertType.AiMonitoringUnavailable
    ];
    public static string Title(ProctoringLog log) =>
        log.IncidentKey is "absence:escalation" or "persons:escalation"
            ? log.Description ?? Title(log.AlertType)
            : log.IncidentKey?.StartsWith("object:", StringComparison.Ordinal) == true
                ? $"Object Detection — {log.ModelName ?? "Object under review"}" : Title(log.AlertType);
    public static string Title(ProctoringAlertType type) => type switch {
        ProctoringAlertType.LookingAwaySpeaking => "Looking Away and Speaking",
        ProctoringAlertType.HeadAway or ProctoringAlertType.GazeAway or ProctoringAlertType.LookAway => "Looking Away",
        ProctoringAlertType.AudioDetected => "Speech Observed — Speaker Unknown",
        ProctoringAlertType.TalkingDetected => "Student Speaking",
        ProctoringAlertType.AnswerPromptSpeech => "Exam-Related Conversation",
        ProctoringAlertType.MultipleFaces => "Multiple Faces on Single Screen",
        ProctoringAlertType.AdditionalPersonObservation => "Additional Person — Participation Uncertain",
        ProctoringAlertType.IdentityMismatch => "Student Changed",
        ProctoringAlertType.ElectronicDeviceDetected => "Possible Unauthorized Object",
        ProctoringAlertType.FaceNotVisible => "Face Not Visible",
        ProctoringAlertType.FaceNotDetected => "Student Absence",
        ProctoringAlertType.CameraMonitoringInterrupted => "Camera Monitoring Interrupted",
        ProctoringAlertType.TabSwitch => "Tab Switching",
        ProctoringAlertType.ExamPageHidden => "Tab Switching — Page Visibility Observation",
        ProctoringAlertType.ExamWindowLeft => "Tab Switching — Focus Observation",
        ProctoringAlertType.ScreenShareLost => "Screen Sharing Interrupted",
        ProctoringAlertType.ScreenCaptureUnavailable => "Screen Capture Unavailable",
        ProctoringAlertType.FullScreenExit => "Fullscreen Exited",
        ProctoringAlertType.AiMonitoringUnavailable or ProctoringAlertType.CameraAccessLost or ProctoringAlertType.MicrophoneAccessLost => "Monitoring Technical Issue",
        ProctoringAlertType.ExamBlocked => "Exam Blocked for Review",
        ProctoringAlertType.ExamUnblocked or ProctoringAlertType.ExamResumed => "Exam Resumed",
        ProctoringAlertType.ReviewRequested => "Review Update",
        _ => "Examination Observation"
    };
    public static string Category(ProctoringLog log) => log.AlertType is ProctoringAlertType.ScreenShareLost or ProctoringAlertType.ScreenCaptureUnavailable or ProctoringAlertType.CameraMonitoringInterrupted or ProctoringAlertType.AiMonitoringUnavailable or ProctoringAlertType.CameraAccessLost or ProctoringAlertType.MicrophoneAccessLost
        ? "technical" : log.CountsAsViolation ? "violation" : log.AlertType is ProctoringAlertType.ExamBlocked or ProctoringAlertType.ExamUnblocked or ProctoringAlertType.ExamResumed ? "system" : "review";
}
