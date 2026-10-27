namespace AiExamProctoring.Services.FaceIdentity;

public sealed class FaceIdentityOptions
{
    public const string SectionName = "FaceIdentity";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:8001/";
    public string ApiKey { get; set; } = string.Empty;
    public int EnrollmentMinimumImages { get; set; } = 5;
    public int EnrollmentMaximumImages { get; set; } = 10;
    public int VerificationFrames { get; set; } = 5;
    public int ContinuousVerificationFrames { get; set; } = 3;
    public int VerificationTicketMinutes { get; set; } = 5;
    public int ContinuousVerificationSeconds { get; set; } = 60;
    public string ModelName { get; set; } = "InsightFace buffalo_l (det_10g + w600k_r50)";
    public string ModelVersion { get; set; } = "Model1-v2-gallery-provisional";
}
