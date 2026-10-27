using AiExamProctoring.Models;

namespace AiExamProctoring.Services.Proctoring;

public static class BlockingPolicy
{
    public static bool ShouldAutoLock(ExamBlockingMode mode, int strikes, bool confirmedIdentityMismatch) =>
        mode is ExamBlockingMode.AUTO_ONLY or ExamBlockingMode.BOTH && (confirmedIdentityMismatch || strikes >= 4);
    public static bool CanManuallyBlock(ExamBlockingMode mode) =>
        mode is ExamBlockingMode.MANUAL_ONLY or ExamBlockingMode.BOTH;
}
