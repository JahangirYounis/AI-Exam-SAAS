namespace AiExamProctoring.Services.BehaviorAnalysis;

public sealed class BehaviorAnalysisOptions
{
    public const string SectionName = "BehaviorAnalysis";
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8002/";
    public string AbsenceBaseUrl { get; set; } = "http://127.0.0.1:8005/";
    public int AttentionTimeoutMilliseconds { get; set; } = 4000;
    public int AbsenceTimeoutMilliseconds { get; set; } = 2500;
    public int MultiplePeopleTimeoutMilliseconds { get; set; } = 4000;
    public string MultiplePeopleBaseUrl { get; set; } = "http://127.0.0.1:8006/";
    public int PreCheckFrames { get; set; } = 5;
    public int BaselineFrames { get; set; } = 8;
    public int FrameIntervalMilliseconds { get; set; } = 500;
    public string ModelName { get; set; } = "MediaPipe Face Landmarker";
    public string ModelVersion { get; set; } = "Model2-v1-provisional";
    public bool ObservationOnly { get; set; } = true;
}
