namespace AiExamProctoring.Services.ObjectDetection;

public sealed class ObjectDetectionOptions
{
    public const string SectionName = "ObjectDetection";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8003/";
    public string ApiKey { get; set; } = string.Empty;
    public int FrameIntervalMilliseconds { get; set; } = 3_000;
    public decimal ConfidenceThreshold { get; set; } = 0.45m;
    public string ModelName { get; set; } = "YOLOv8 Nano ONNX object observations";
    public string ModelVersion { get; set; } = "Model3-v8-13class-exam-objects";
    public string[] MonitoredClasses { get; set; } = ["mobile_phone", "book", "laptop", "extra_person", "earphones", "monitor", "tablet", "smart_watch", "calculator", "notes_paper", "pen_pencil", "bag", "bottle"];
    public bool ObservationOnly { get; set; } = true;
}
