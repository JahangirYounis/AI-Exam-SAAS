using AiExamProctoring.Services.FaceIdentity;
using System.Text.Json.Serialization;

namespace AiExamProctoring.Services.BehaviorAnalysis;

public sealed class BehaviorResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("started")] public bool Started { get; set; }
    [JsonPropertyName("ended")] public bool Ended { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("detail")] public object? Detail { get; set; }
    [JsonPropertyName("module_version")] public string? ModuleVersion { get; set; }
    [JsonPropertyName("calibration_status")] public string? CalibrationStatus { get; set; }
    [JsonPropertyName("observation_only")] public bool ObservationOnly { get; set; }
    [JsonPropertyName("accepted_frames")] public int AcceptedFrames { get; set; }
    [JsonPropertyName("total_frames")] public int TotalFrames { get; set; }
    [JsonPropertyName("maximum_face_count")] public int MaximumFaceCount { get; set; }
    [JsonPropertyName("metrics")] public BehaviorMetrics? Metrics { get; set; }
    [JsonPropertyName("events")] public List<BehaviorEvent> Events { get; set; } = new();
    [JsonPropertyName("signal_codes")] public Dictionary<string, string> SignalCodes { get; set; } = new();
    [JsonPropertyName("signals")] public Dictionary<string, bool?>? Signals { get; set; }
}

public sealed class BehaviorMetrics
{
    [JsonPropertyName("presentation_status")] public string PresentationStatus { get; set; } = "not_checked";
    [JsonPropertyName("ignored_background_regions")] public int IgnoredBackgroundRegions { get; set; }
    [JsonPropertyName("face_count")] public int FaceCount { get; set; }
    [JsonPropertyName("brightness")] public double Brightness { get; set; }
    [JsonPropertyName("blur_variance")] public double BlurVariance { get; set; }
    [JsonPropertyName("head_pose_ok")] public bool? HeadPoseOk { get; set; }
    [JsonPropertyName("quality_reason")] public string? QualityReason { get; set; }
    [JsonPropertyName("gaze_left")] public double GazeLeft { get; set; }
    [JsonPropertyName("gaze_right")] public double GazeRight { get; set; }
    [JsonPropertyName("gaze_up")] public double GazeUp { get; set; }
    [JsonPropertyName("gaze_down")] public double GazeDown { get; set; }
    [JsonPropertyName("quality_ok")] public bool QualityOk { get; set; }
    [JsonPropertyName("yaw")] public double Yaw { get; set; }
    [JsonPropertyName("pitch")] public double Pitch { get; set; }
    [JsonPropertyName("roll")] public double Roll { get; set; }
    [JsonPropertyName("eye_closed")] public double EyeClosed { get; set; }
    [JsonPropertyName("mouth_movement")] public double MouthMovement { get; set; }
}

public sealed class BehaviorEvent
{
    [JsonPropertyName("direction")] public string? Direction { get; set; }
    [JsonPropertyName("event_type")] public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("duration_seconds")] public double DurationSeconds { get; set; }
    [JsonPropertyName("confidence")] public decimal Confidence { get; set; }
}

public sealed record BehaviorCallResult(bool Success, BehaviorResponse? Data, string Message, bool ServiceUnavailable = false);

public sealed class BehaviorFrameRequest
{
    public bool ScreenCalibration { get; set; }
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }
    public List<IFormFile> Frames { get; set; } = new();
}

public sealed class BehaviorSingleFrameRequest
{
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }
    public IFormFile? Frame { get; set; }
}
