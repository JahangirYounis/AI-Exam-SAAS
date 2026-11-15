using AiExamProctoring.Models;
namespace AiExamProctoring.Services.Proctoring;
public static class AbsenceReturnPolicy {
    public static bool PhysicallyPresent(string? state, bool detectorReady, bool cameraUsable) =>
        detectorReady && cameraUsable && state == "PERSON_VISIBLE_IDENTITY_UNCERTAIN";

    public static bool Verified(int? faces, ProctoringLog? latestIdentity, DateTime? absenceStartedUtc, DateTime nowUtc) {
        if (faces != 1 || latestIdentity?.AlertType != ProctoringAlertType.IdentityVerified || latestIdentity.FrameCapturedAt is not DateTime captured || absenceStartedUtc is null) return false;
        var verifiedAt=captured.ToUniversalTime();
        return verifiedAt > absenceStartedUtc && verifiedAt <= nowUtc && nowUtc-verifiedAt <= TimeSpan.FromSeconds(30);
    }
}
