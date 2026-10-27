using System.Text.Json.Serialization;

namespace AiExamProctoring.Services.FaceIdentity;

public sealed record FaceIdentityImage(string FileName, string ContentType, byte[] Bytes);

public sealed class FaceIdentityResponse
{
    [JsonPropertyName("best_photo_index")] public int BestPhotoIndex { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("exists")] public bool Exists { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("reason_code")] public string? ReasonCode { get; set; }
    [JsonPropertyName("module_name")] public string? ModuleName { get; set; }
    [JsonPropertyName("module_version")] public string? ModuleVersion { get; set; }
    [JsonPropertyName("threshold_status")] public string? ThresholdStatus { get; set; }
    [JsonPropertyName("photo_count")] public int PhotoCount { get; set; }
    [JsonPropertyName("image_count")] public int ImageCount { get; set; }
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("passing_frames")] public int PassingFrames { get; set; }
    [JsonPropertyName("vote_ratio")] public decimal VoteRatio { get; set; }
    [JsonPropertyName("median_similarity")] public decimal MedianSimilarity { get; set; }
    [JsonPropertyName("minimum_similarity")] public decimal MinimumSimilarity { get; set; }
    [JsonPropertyName("maximum_similarity")] public decimal MaximumSimilarity { get; set; }
    [JsonPropertyName("threshold")] public decimal Threshold { get; set; }
    [JsonPropertyName("average_detection_confidence")] public decimal AverageDetectionConfidence { get; set; }
    [JsonPropertyName("minimum_pairwise_similarity")] public decimal MinimumPairwiseSimilarity { get; set; }
    [JsonPropertyName("average_pairwise_similarity")] public decimal AveragePairwiseSimilarity { get; set; }
}

public sealed record FaceIdentityCallResult(
    bool Success,
    FaceIdentityResponse? Data,
    string Message,
    bool ServiceUnavailable = false);

public sealed class FaceIdentityFrameRequest
{
    public int ExamId { get; set; }
    public int AttemptId { get; set; }
    public int SessionAttemptId { get; set; }
    public List<IFormFile> Frames { get; set; } = new();
}

public sealed class FaceEnrollmentRequest
{
    public List<IFormFile> Images { get; set; } = new();
    public string? CapturedImagesJson { get; set; }
    public string? ReturnUrl { get; set; }
}
