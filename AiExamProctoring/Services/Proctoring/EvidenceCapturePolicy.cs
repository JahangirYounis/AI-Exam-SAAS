using AiExamProctoring.Models;

namespace AiExamProctoring.Services.Proctoring;

/// <summary>
/// Limits MEDIA ATTACHMENTS per confirmed incident, never the incident itself or
/// independently detected rule violations. Each accepted asset remains immutable.
/// Called inside the attempt write lock; client throttles are only an optimization.
/// </summary>
public static class EvidenceCapturePolicy
{
    public static int LimitForStage(string stage, ProctoringSeverity severity) => stage switch
    {
        "before" or "during" or "return" or "screen-image" => 1,
        "composite" => 1,
        "camera-original" or "screen-original" or "audio-original" =>
            severity == ProctoringSeverity.High ? 3 : 1,
        "video" => severity == ProctoringSeverity.High ? 3 : 2,
        _ => 0
    };

    public static bool MayStore(string stage, ProctoringSeverity severity,
        int sameStageCount, int totalAttachmentCount) =>
        sameStageCount < LimitForStage(stage, severity) &&
        totalAttachmentCount < (severity == ProctoringSeverity.High ? 18 : 10);
}
