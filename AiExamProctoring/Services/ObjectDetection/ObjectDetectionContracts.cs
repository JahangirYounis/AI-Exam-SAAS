using System.Text.Json.Serialization;

namespace AiExamProctoring.Services.ObjectDetection;

public sealed class ObjectDetectionResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("detail")] public object? Detail { get; set; }
    [JsonPropertyName("module_version")] public string? ModuleVersion { get; set; }
    [JsonPropertyName("observation_only")] public bool ObservationOnly { get; set; }
    [JsonPropertyName("detection_count")] public int DetectionCount { get; set; }
    [JsonPropertyName("detections")] public List<ObjectDetectionItem> Detections { get; set; } = new();
}

public sealed class ObjectDetectionItem
{
    [JsonPropertyName("alert_eligible")] public bool AlertEligible { get; set; }
    [JsonPropertyName("context_reason")] public string? ContextReason { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("class_id")] public int ClassId { get; set; }
    [JsonPropertyName("confidence")] public decimal Confidence { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

public sealed record ObjectDetectionCallResult(
    bool Success,
    ObjectDetectionResponse? Data,
    string Message,
    bool ServiceUnavailable = false);

public sealed class ObjectDetectionFrameRequest
{
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }
    public IFormFile? Frame { get; set; }
}
