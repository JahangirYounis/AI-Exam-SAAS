using System.Text.Json;
using System.Text.Json.Nodes;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Student.Controllers;

public partial class ExamController
{
    private static JsonObject SpeechMetadata(string? value)
    {
        try { return JsonNode.Parse(value ?? "{}") as JsonObject ?? new(); }
        catch (JsonException) { return new(); }
    }

    private async Task CaptureSpeechQuestions(ProctoringLog log, IEnumerable<Question> questions)
    {
        foreach (var q in questions)
        {
            var key = $"speech-q:{log.LogId}:{q.QuestionId}";
            if (await _context.ProctoringLogs.AnyAsync(l => l.ClientEventId == key)) continue;
            // Keep original server-owned text, never navigation state at ASR completion.
            var subject = q.Subject?.SubjectName ?? (q.SubjectId > 0 ? await _context.Subjects
                .Where(s => s.SubjectId == q.SubjectId).Select(s => s.SubjectName).FirstOrDefaultAsync() : null);
            _context.ProctoringLogs.Add(new ProctoringLog {
                ParentIncidentId=log.LogId, StudentId=log.StudentId, ExamId=log.ExamId,
                AttemptId=log.AttemptId, SessionAttemptId=log.SessionAttemptId, ClientEventId=key,
                AlertType=ProctoringAlertType.Other, ModelName="Speech question capture",
                CountsAsViolation=false, PolicyProcessed=true, QuestionIdAtCapture=q.QuestionId,
                CreatedDate=DateTime.UtcNow, FrameCapturedAt=DateTime.UtcNow,
                MachineObservationsJson=JsonSerializer.Serialize(new {
                    q.QuestionId,q.QuestionText,q.OptionA,q.OptionB,q.OptionC,q.OptionD,q.OptionE,q.SubjectId,
                    Subject = subject is null ? null : new { SubjectName = subject }
                })
            });
        }
    }

    private async Task<Question?> CapturedSpeechQuestion(ProctoringLog? log, int questionId)
    {
        var key=$"speech-q:{log?.LogId}:{questionId}";
        var snapshot=await _context.ProctoringLogs.AsNoTracking().Where(l=>l.ClientEventId==key && l.ParentIncidentId==log!.LogId)
            .Select(l=>l.MachineObservationsJson).FirstOrDefaultAsync();
        if(snapshot is not null)return JsonSerializer.Deserialize<Question>(snapshot);
        var captures = SpeechMetadata(log?.MachineObservationsJson)["captureContexts"] as JsonObject;
        return captures?[questionId.ToString(System.Globalization.CultureInfo.InvariantCulture)]?.Deserialize<Question>();
    }

    private static void ObserveSpeechQuiet(ProctoringLog log, DateTime now)
    {
        if (log.RecoveryStatus == "Ended" || now < (log.IncidentEndedAtUtc ?? log.IncidentStartedAtUtc)) return;
        var metadata = SpeechMetadata(log.MachineObservationsJson);
        DateTime? Read(string field) => metadata[field] is JsonValue value &&
            value.TryGetValue<DateTime>(out var date) ? date.ToUniversalTime() : null;
        var since = Read("quietSinceUtc");
        var last = Read("lastQuietUtc");
        if (last.HasValue && now <= last.Value) return;
        if (!last.HasValue || (now - last.Value).TotalSeconds > 3) since = now;
        metadata["quietSinceUtc"] = since ?? now;
        metadata["lastQuietUtc"] = now;
        if ((now - (since ?? now)).TotalSeconds >= 3) log.RecoveryStatus = "Ended";
        log.MachineObservationsJson = metadata.ToJsonString();
    }

    private static string EnrichSpeechMetadata(string? previous, string update)
    {
        var before = SpeechMetadata(previous);
        var after = SpeechMetadata(update);
        if (before["captureContexts"] is JsonNode captures) after["captureContexts"] = captures.DeepClone();
        foreach (var key in new[] { "quietSinceUtc", "lastQuietUtc" })
            if (before[key] is JsonNode value) after[key] = value.DeepClone();
        return after.ToJsonString();
    }
}
