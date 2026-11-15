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
            _context.ProctoringLogs.Add(new ProctoringLog {
                ParentIncidentId=log.LogId, StudentId=log.StudentId, ExamId=log.ExamId,
                AttemptId=log.AttemptId, SessionAttemptId=log.SessionAttemptId, ClientEventId=key,
                AlertType=ProctoringAlertType.Other, ModelName="Speech question capture",
                CountsAsViolation=false, PolicyProcessed=true, QuestionIdAtCapture=q.QuestionId,
                CreatedDate=DateTime.UtcNow, FrameCapturedAt=DateTime.UtcNow,
                MachineObservationsJson=JsonSerializer.Serialize(new {
                    q.QuestionId,q.QuestionText,q.OptionA,q.OptionB,q.OptionC,q.OptionD,q.OptionE
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

    private static string EnrichSpeechMetadata(string? previous, string update)
    {
        var before = SpeechMetadata(previous);
        var after = SpeechMetadata(update);
        if (before["captureContexts"] is JsonNode captures) after["captureContexts"] = captures.DeepClone();
        return after.ToJsonString();
    }
}
