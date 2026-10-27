using System.Text.Json.Serialization;

namespace AiExamProctoring.Services.SpeechRecognition;

public sealed class SpeechRecognitionResponse
{
    [JsonPropertyName("context_analysis")] public SpeechContextAnalysis? ContextAnalysis { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("detail")] public object? Detail { get; set; }
    [JsonPropertyName("module_version")] public string? ModuleVersion { get; set; }
    [JsonPropertyName("model_sha256")] public string? ModelSha256 { get; set; }
    [JsonPropertyName("observation_only")] public bool ObservationOnly { get; set; }
    [JsonPropertyName("speech_detected")] public bool SpeechDetected { get; set; }
    [JsonPropertyName("vad_speech_seconds")] public double VadSpeechSeconds { get; set; }
    [JsonPropertyName("transcript")] public string Transcript { get; set; } = string.Empty;
    [JsonPropertyName("language")] public string Language { get; set; } = string.Empty;
    [JsonPropertyName("language_probability")] public decimal LanguageProbability { get; set; }
    [JsonPropertyName("confidence")] public decimal Confidence { get; set; }
    [JsonPropertyName("duration_seconds")] public decimal DurationSeconds { get; set; }
    [JsonPropertyName("segments")] public List<SpeechRecognitionSegment> Segments { get; set; } = new();
}

public sealed class SpeechRecognitionSegment
{
    [JsonPropertyName("start")] public decimal Start { get; set; }
    [JsonPropertyName("end")] public decimal End { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("confidence")] public decimal Confidence { get; set; }
    [JsonPropertyName("no_speech_probability")] public decimal NoSpeechProbability { get; set; }
}

public sealed record SpeechQuestionContext(
    [property: JsonPropertyName("question_id")] int QuestionId,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("options")] string[] Options);

public sealed class SpeechContextAnalysis
{
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("related")] public bool Related { get; set; }
    [JsonPropertyName("uncertain")] public bool Uncertain { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("score")] public double? Score { get; set; }
    [JsonPropertyName("question_id")] public int? QuestionId { get; set; }
    [JsonPropertyName("match_kind")] public string? MatchKind { get; set; }
    [JsonPropertyName("elapsed_ms")] public double? ElapsedMs { get; set; }
}

public sealed record SpeechAudio(byte[] Bytes, string ContentType, string FileName)
{
    public bool DetectionOnly { get; init; }
    public IReadOnlyList<SpeechQuestionContext> Contexts { get; init; } = Array.Empty<SpeechQuestionContext>();
}

public sealed record SpeechRecognitionCallResult(
    bool Success,
    SpeechRecognitionResponse? Data,
    string Message,
    bool ServiceUnavailable = false);

public sealed class SpeechRecognitionRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(300000)]
    public string? QuestionScreenshotDataUrl { get; set; }
    public int? DetectionLogId { get; set; }
    public List<int> QuestionIds { get; set; } = new();
    public int? QuestionId { get; set; }
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }
    public IFormFile? Audio { get; set; }
}
