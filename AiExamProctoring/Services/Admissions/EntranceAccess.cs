using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Admissions;

public static class EntranceAccess
{
    public static IQueryable<int> InvitedExams(ApplicationDbContext db, string? teacher) =>
        db.ExamInvigilators.Where(x => x.TeacherId == teacher && x.IsActive && x.CanMonitor && x.AcceptedAtUtc != null).Select(x => x.ExamId);

    public static IQueryable<int> ManagedExams(ApplicationDbContext db, string? teacher) =>
        db.Exams.Where(x => x.Purpose == ExamPurpose.Entrance &&
            (x.CreatedBy == teacher || InvitedExams(db, teacher).Contains(x.ExamId))).Select(x => x.ExamId);

    public static IQueryable<string> AssignedStudents(ApplicationDbContext db, int examId, string? teacher) =>
        db.AdmissionApplications.Where(x => x.ExamId == examId && x.InvigilatorId == teacher &&
            x.Status == AdmissionStatus.Approved && x.PaymentStatus == AdmissionPaymentStatus.Verified &&
            db.ExamInvigilators.Any(i => i.ExamId == examId && i.TeacherId == teacher && i.IsActive && i.CanMonitor && i.AcceptedAtUtc != null)).Select(x => x.StudentId);

    public static Task<bool> CanReviewAsync(ApplicationDbContext db, int examId, string studentId, string? teacher, CancellationToken ct) =>
        db.Exams.AnyAsync(e => e.ExamId == examId && e.Purpose == ExamPurpose.Entrance &&
            (e.CreatedBy == teacher || AssignedStudents(db, examId, teacher).Contains(studentId)), ct);

    public static async Task<string[]> TeachersAsync(ApplicationDbContext db, int examId, CancellationToken ct = default)
    {
        var owner = await db.Exams.Where(x => x.ExamId == examId).Select(x => x.CreatedBy).SingleAsync(ct);
        var ids = await db.ExamInvigilators.Where(x => x.ExamId == examId && x.IsActive && x.CanMonitor && x.AcceptedAtUtc != null).Select(x => x.TeacherId).ToListAsync(ct);
        if (!string.IsNullOrEmpty(owner)) ids.Add(owner);
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }
}
