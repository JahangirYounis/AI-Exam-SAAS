using AiExamProctoring.Data;
using AiExamProctoring.Models;
using ExamModel = AiExamProctoring.Models.Exam;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Shared server-side evidence boundary. Never trust a ProctoringLog.StudentId as
/// proof of a candidate: audit entries can legitimately identify a teacher.
/// Evidence is reachable only through an actual attempt owned by a Student-role
/// account, in an exam owned by this teacher or explicitly shared for monitoring.
/// </summary>
public static class TeacherEvidenceAccess
{
    private static readonly ProctoringAlertType[] ReviewKinds =
    [
        ProctoringAlertType.IdentityMismatch, ProctoringAlertType.FaceNotDetected,
        ProctoringAlertType.FaceNotVisible, ProctoringAlertType.MultipleFaces,
        ProctoringAlertType.AdditionalPersonObservation,
        ProctoringAlertType.LookAway, ProctoringAlertType.HeadAway,
        ProctoringAlertType.GazeAway, ProctoringAlertType.EyesClosed,
        ProctoringAlertType.MouthMovement, ProctoringAlertType.TalkingDetected,
        ProctoringAlertType.AnswerPromptSpeech, ProctoringAlertType.AudioDetected,
        ProctoringAlertType.ElectronicDeviceDetected, ProctoringAlertType.ObjectObservation,
        ProctoringAlertType.TabSwitch, ProctoringAlertType.FullScreenExit,
        ProctoringAlertType.ExamPageHidden, ProctoringAlertType.ExamWindowLeft,
        ProctoringAlertType.ScreenShareLost, ProctoringAlertType.MultipleDisplaysDetected,
        ProctoringAlertType.ScreenshotAttempt, ProctoringAlertType.CameraAccessLost,
        ProctoringAlertType.MicrophoneAccessLost, ProctoringAlertType.CameraMonitoringInterrupted,
        ProctoringAlertType.AiMonitoringUnavailable, ProctoringAlertType.ExamBlocked,
        ProctoringAlertType.ReviewRequested, ProctoringAlertType.LookingAwaySpeaking
    ];

   public static IQueryable<ExamModel> Exams(
    ApplicationDbContext db, string teacherId) =>
        db.Exams.Where(e => !string.IsNullOrEmpty(teacherId) && (e.CreatedBy == teacherId ||
            db.ExamInvigilators.Any(i => i.ExamId == e.ExamId &&
                i.TeacherId == teacherId && i.IsActive && i.CanMonitor &&
                i.AcceptedAtUtc != null && i.RevokedAtUtc == null)));

    public static IQueryable<ExamAttempt> Attempts(ApplicationDbContext db, string teacherId) =>
        db.ExamAttempts.Where(a => !string.IsNullOrEmpty(teacherId) && a.Exam != null &&
            (a.Exam.CreatedBy == teacherId ||
             db.ExamInvigilators.Any(i => i.ExamId == a.ExamId &&
                 i.TeacherId == teacherId && i.IsActive && i.CanMonitor &&
                 i.AcceptedAtUtc != null && i.RevokedAtUtc == null)) &&
            db.UserRoles.Any(ur => ur.UserId == a.StudentId &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Student")));

    public static IQueryable<ProctoringLog> Incidents(ApplicationDbContext db, string teacherId) =>
        db.ProctoringLogs.Where(l => !string.IsNullOrEmpty(teacherId) &&
            l.AttemptId.HasValue && l.Attempt != null &&
            l.ExamId == l.Attempt.ExamId && l.StudentId == l.Attempt.StudentId &&
            l.AlertType != ProctoringAlertType.SystemAudit &&
            (l.Attempt.Exam!.CreatedBy == teacherId ||
             db.ExamInvigilators.Any(i => i.ExamId == l.Attempt.ExamId &&
                 i.TeacherId == teacherId && i.IsActive && i.CanMonitor &&
                 i.AcceptedAtUtc != null && i.RevokedAtUtc == null)) &&
            db.UserRoles.Any(ur => ur.UserId == l.Attempt.StudentId &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Student")));

    public static IQueryable<ProctoringLog> ReviewableIncidents(ApplicationDbContext db, string teacherId) =>
        Incidents(db, teacherId).Where(l => l.ModelName != "Teacher review" &&
            (l.CountsAsViolation || ReviewKinds.Contains(l.AlertType)));

    // Ordinary observation review must never silently dismiss a blocked-exam
    // decision or the student's explicit appeal. Those are separate workflows.
    public static IQueryable<ProctoringLog> ObservationIncidents(ApplicationDbContext db, string teacherId) =>
        ReviewableIncidents(db, teacherId).Where(l =>
            l.AlertType != ProctoringAlertType.ExamBlocked &&
            !(l.AlertType == ProctoringAlertType.ReviewRequested && l.ModelName == "Student appeal"));
}
