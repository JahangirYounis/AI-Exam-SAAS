namespace AiExamProctoring.Services.SpeechRecognition;

public sealed class SpeechRecognitionOptions
{
    public const string SectionName = "SpeechRecognition";
    public bool TalkingProhibited { get; set; } = true;
    public bool AllowRemoteQuestionContext { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8004/";
    public string ContextBaseUrl { get; set; } = "http://127.0.0.1:8007/";
    public string ApiKey { get; set; } = string.Empty;
    public int ClipSeconds { get; set; } = 8;
    public int AnalysisIntervalMilliseconds { get; set; } = 20_000;
    public int MaximumAudioBytes { get; set; } = 12_000_000;
    public decimal MinimumTranscriptConfidence { get; set; } = 0.30m;
    public string ModelName { get; set; } = "faster-whisper tiny multilingual";
    public string ModelVersion { get; set; } = "Model4-v2-faster-whisper-tiny-vad";
    public bool ObservationOnly { get; set; } = true;
}
