using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>Append-only access audit. Keep system-audit logs out of candidate review queues.</summary>
public static class EvidenceAccessAudit
{
    public static async Task RecordAsync(ApplicationDbContext db, ProctoringLog incident,
        string actorId, string kind, bool download, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorId) || incident.AttemptId is null)
            throw new UnauthorizedAccessException();
        var action = download ? "Downloaded" : "Viewed";
        // Avoid a flood of audit entries from browser range/preview requests.
        // Still record a new event when a new explicit action occurs later.
        var recent = await db.ProctoringLogs.AsNoTracking().AnyAsync(l =>
            l.ParentIncidentId == incident.LogId &&
            l.ModelName == "Evidence access" && l.ActionByUserId == actorId &&
            l.Description == $"{action} selected {kind} evidence" &&
            l.CreatedDate >= DateTime.Now.AddSeconds(-30), ct);
        if (recent) return;
        db.ProctoringLogs.Add(new ProctoringLog
        {
            StudentId = incident.StudentId,
            ExamId = incident.ExamId,
            AttemptId = incident.AttemptId,
            SessionAttemptId = incident.SessionAttemptId,
            ParentIncidentId = incident.LogId,
            AlertType = ProctoringAlertType.SystemAudit,
            Severity = ProctoringSeverity.Low,
            ModelName = "Evidence access",
            Description = $"{action} selected {kind} evidence",
            MachineObservationsJson = JsonSerializer.Serialize(new {
                action, kind, actorId, targetLogId = incident.LogId,
                atUtc = DateTime.UtcNow
            }),
            ActionByUserId = actorId,
            CountsAsViolation = false,
            PolicyProcessed = true,
            IsReviewed = true,
            CreatedDate = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
    }
}
