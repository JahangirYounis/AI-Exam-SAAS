using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiExamProctoring.Models
{
    public enum ProctoringAlertType
    {
        IdentityVerified,
        IdentityMismatch,
        FaceNotDetected,
        MultipleFaces,
        LookAway,
        AudioDetected,
        TalkingDetected,
        AnswerPromptSpeech,
        TabSwitch,
        FullScreenExit,
        CameraAccessLost,
        MicrophoneAccessLost,
        AiMonitoringUnavailable,
        ExamBlocked,
        ExamUnblocked,
        Other,
        // Appended for source compatibility; EF persists these alert names as strings.
        ScreenShareLost,
        MultipleDisplaysDetected,
        ScreenshotAttempt,
        ElectronicDeviceDetected,
        PolicyHoldReleased,
        FaceRestored,
        MultipleFacesCleared,
        IdentityRestored,
        AttentionRestored,
        ElectronicDeviceCleared,
        HeadAway,
        GazeAway,
        EyesClosed,
        MouthMovement,
        ReminderIssued,
        SessionRecording,
        ExamResumed,
        ReviewRequested
    }

    public enum ProctoringSeverity
    {
        Low,
        Medium,
        High
    }

    public class ProctoringLog
    {
    [StringLength(320)] public string? VideoEvidencePath { get; set; }
    [StringLength(4000)] public string? SpeechTranscript { get; set; }
    [Column(TypeName = "decimal(7,6)")] public decimal? SemanticSimilarity { get; set; }
    public int? MatchedQuestionId { get; set; }
    [StringLength(40)] public string? SpeakerAttribution { get; set; }

        [Key]
        public int LogId { get; set; }

        //==========================================================
        // STUDENT
        //==========================================================

        [Required]
        public string StudentId { get; set; } = string.Empty;

        [ForeignKey(nameof(StudentId))]
        public virtual ApplicationUser? Student { get; set; }

        //==========================================================
        // EXAM
        //==========================================================

        public int? ExamId { get; set; }

        [ForeignKey(nameof(ExamId))]
        public virtual Exam? Exam { get; set; }

        //==========================================================
        // ATTEMPT
        //==========================================================

        public int? AttemptId { get; set; }

        [ForeignKey(nameof(AttemptId))]
        public virtual ExamAttempt? Attempt { get; set; }

        public int? SessionAttemptId { get; set; }
        public int? QuestionIdAtCapture { get; set; }
        public int? QuestionIndexAtCapture { get; set; }

        [ForeignKey(nameof(SessionAttemptId))]
        public virtual ExamSessionAttempt? SessionAttempt { get; set; }

        //==========================================================
        // ALERT
        //==========================================================

        [Required]
        public ProctoringAlertType AlertType { get; set; }

        [Required]
        public ProctoringSeverity Severity { get; set; }
            = ProctoringSeverity.Low;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(500)]
        // Internal App_Data storage key, never a public wwwroot URL.
        public string? ScreenshotPath { get; set; }

        [StringLength(500)]
        // Short camera recording kept separately so attaching a video never
        // deletes the still screenshot. Entire Screen sharing is not required.
        public string? RecordingPath { get; set; }

        public bool RecordingIncludesAudio { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? ConfidenceScore { get; set; }

        public int? DetectedFaceCount { get; set; }

        public int? DurationSeconds { get; set; }

        [StringLength(100)]
        public string? ModelName { get; set; }

        [StringLength(100)]
        public string? ModelVersion { get; set; }

        [StringLength(64)]
        public string? ClientEventId { get; set; }

        public DateTime? FrameCapturedAt { get; set; }

        public bool CountsAsViolation { get; set; } = true;
        public bool PolicyProcessed { get; set; }
        public bool PolicyPending { get; set; }
        [StringLength(32)] public string? IncidentKey { get; set; }
        public int? WarningNumber { get; set; }

        [StringLength(450)] public string? ActionByUserId { get; set; }
        [StringLength(256)] public string? ActionByName { get; set; }
        public bool IsReviewed { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        //==========================================================
        // DISPLAY
        //==========================================================

        [NotMapped]
        public string AlertTypeDisplay =>
            AlertType switch
            {
                ProctoringAlertType.FaceNotDetected =>
                    "Face Not Detected",

                ProctoringAlertType.IdentityVerified =>
                    "Identity Verified",

                ProctoringAlertType.IdentityMismatch =>
                    "Identity Mismatch",

                ProctoringAlertType.MultipleFaces =>
                    "Multiple Faces",

                ProctoringAlertType.LookAway =>
                    "Looking Away",

                ProctoringAlertType.AudioDetected =>
                    "Audio Detected",

                ProctoringAlertType.TalkingDetected =>
                    "Talking Detected",

                ProctoringAlertType.AnswerPromptSpeech =>
                    "Answer-Related Speech",

                ProctoringAlertType.TabSwitch =>
                    "Tab Switch",

                ProctoringAlertType.FullScreenExit =>
                    "Full Screen Exit",

                ProctoringAlertType.CameraAccessLost =>
                    "Camera Access Lost",

                ProctoringAlertType.MicrophoneAccessLost =>
                    "Microphone Access Lost",

                ProctoringAlertType.AiMonitoringUnavailable =>
                    "AI Monitoring Unavailable",

                ProctoringAlertType.ScreenShareLost =>
                    "Screen Sharing Lost",

                ProctoringAlertType.MultipleDisplaysDetected =>
                    "Multiple Displays Detected",

                ProctoringAlertType.ScreenshotAttempt =>
                    "Screenshot / Print Attempt",

                ProctoringAlertType.ElectronicDeviceDetected =>
                    "Device / Reference Material Detected",

                ProctoringAlertType.PolicyHoldReleased =>
                    "Policy Hold Released",

                ProctoringAlertType.FaceRestored => "Face Restored",
                ProctoringAlertType.MultipleFacesCleared => "Additional Person Left",
                ProctoringAlertType.IdentityRestored => "Verified Identity Restored",
                ProctoringAlertType.AttentionRestored => "Attention Restored",
                ProctoringAlertType.ElectronicDeviceCleared => "Electronic Device Removed",
                ProctoringAlertType.HeadAway => "Head Turned Away",
                ProctoringAlertType.GazeAway => "Gaze Away",
                ProctoringAlertType.EyesClosed => "Eyes Closed",
                ProctoringAlertType.MouthMovement => "Mouth Movement",
                ProctoringAlertType.ReminderIssued => "Candidate Reminder",

                ProctoringAlertType.ExamBlocked =>
                    "Exam Blocked",

                ProctoringAlertType.ExamUnblocked =>
                    "Exam Unblocked",

                _ => "Other"
            };

        [NotMapped]
        public string AlertBadgeClass =>
            AlertType switch
            {
                ProctoringAlertType.IdentityVerified => "bg-success",
                ProctoringAlertType.IdentityMismatch => "bg-danger",
                ProctoringAlertType.FaceNotDetected => "bg-primary",
                ProctoringAlertType.MultipleFaces => "bg-danger",
                ProctoringAlertType.LookAway => "bg-warning text-dark",
                ProctoringAlertType.AudioDetected => "bg-success",
                ProctoringAlertType.TalkingDetected => "bg-info text-dark",
                ProctoringAlertType.AnswerPromptSpeech => "bg-danger",
                ProctoringAlertType.TabSwitch => "bg-warning text-dark",
                ProctoringAlertType.FullScreenExit => "bg-dark",
                ProctoringAlertType.CameraAccessLost => "bg-danger",
                ProctoringAlertType.MicrophoneAccessLost => "bg-danger",
                ProctoringAlertType.AiMonitoringUnavailable => "bg-secondary",
                ProctoringAlertType.ScreenShareLost => "bg-danger",
                ProctoringAlertType.MultipleDisplaysDetected => "bg-danger",
                ProctoringAlertType.ScreenshotAttempt => "bg-danger",
                ProctoringAlertType.ElectronicDeviceDetected => "bg-danger",
                ProctoringAlertType.PolicyHoldReleased => "bg-success",
                ProctoringAlertType.FaceRestored => "bg-success",
                ProctoringAlertType.MultipleFacesCleared => "bg-success",
                ProctoringAlertType.IdentityRestored => "bg-success",
                ProctoringAlertType.AttentionRestored => "bg-success",
                ProctoringAlertType.ElectronicDeviceCleared => "bg-success",
                ProctoringAlertType.HeadAway => "bg-warning text-dark",
                ProctoringAlertType.GazeAway => "bg-warning text-dark",
                ProctoringAlertType.EyesClosed => "bg-info text-dark",
                ProctoringAlertType.MouthMovement => "bg-info text-dark",
                ProctoringAlertType.ExamBlocked => "bg-danger",
                ProctoringAlertType.ExamUnblocked => "bg-success",
                _ => "bg-secondary"
            };

        [NotMapped]
        public string SeverityBadgeClass =>
            Severity switch
            {
                ProctoringSeverity.Low => "bg-secondary",
                ProctoringSeverity.Medium => "bg-warning text-dark",
                ProctoringSeverity.High => "bg-danger",
                _ => "bg-secondary"
            };
    }
}
