using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Results;

/// <summary>
/// One student-owned release boundary for every Student result and document route.
/// A precomputed ExamResult is NOT visible until its attempt is explicitly released.
/// </summary>
public static class StudentResultAccess
{
    public static IQueryable<ExamResult> ReleasedFor(ApplicationDbContext db, string studentId) =>
        db.ExamResults.AsNoTracking().Where(r => r.Attempt != null &&
            r.Attempt.StudentId == studentId && r.Attempt.ResultReleasedAtUtc != null);

    // Apply pass/fail first. High scores can still fail an exam with a higher pass threshold.
    public static string GradeLabel(decimal percentage, decimal passingPercentage)
    {
        if (percentage < passingPercentage) return "Fail";
        if (percentage >= 90m) return "Excellent";
        if (percentage >= 60m) return "Good";
        return "Pass";
    }

    public static bool IsAcademicPass(decimal percentage, decimal passingPercentage) =>
        percentage >= passingPercentage;

    public static string ReviewStatus(ExamAttempt? attempt)
    {
        if (attempt?.ResultReleasedAtUtc is null) return "Pending Teacher Review";
        return string.IsNullOrWhiteSpace(attempt.ResultReviewedBy)
            ? "Result released" : "Reviewed and released";
    }

    public static string CompletionStatus(ExamAttempt? attempt)
    {
        if (attempt is null) return "Unknown";
        if (attempt.Status == AttemptStatus.ProctoringBlocked)
            return "Blocked attempt — not fully completed";
        if (attempt.IsDisqualified || attempt.Status == AttemptStatus.Disqualified)
            return "Integrity restriction — disqualified";
        if (attempt.Status == AttemptStatus.AutoSubmitted)
            return "Automatically submitted";
        return attempt.Status == AttemptStatus.Completed
            ? "Completed" : "Recorded";
    }
}
