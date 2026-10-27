namespace AiExamProctoring.Services.BehaviorAnalysis;

public sealed class BehaviorAnalysisOptions
{
    public const string SectionName = "BehaviorAnalysis";
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8002/";
    public int PreCheckFrames { get; set; } = 5;
    public int BaselineFrames { get; set; } = 8;
    public int FrameIntervalMilliseconds { get; set; } = 500;
    public string ModelName { get; set; } = "MediaPipe Face Landmarker";
    public string ModelVersion { get; set; } = "Model2-v1-provisional";
    public bool ObservationOnly { get; set; } = true;
}
