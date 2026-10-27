using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data;

internal static class AdmissionsModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<UniversityCandidate>(e => {
            e.HasIndex(x => x.StudentId).IsUnique();
            e.HasIndex(x => x.RollNumber).IsUnique().HasFilter("[RollNumber] IS NOT NULL");
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdmissionApplication>(e => {
            e.HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.PaymentReference).IsUnique().HasFilter("[PaymentReference] IS NOT NULL");
            e.HasIndex(x => new { x.ExamId, x.InvigilatorId });
            e.Property(x => x.FeeDue).HasPrecision(10, 2); e.Property(x => x.PaidAmount).HasPrecision(10, 2);
            e.Property(x => x.ObtainedMarks).HasPrecision(10, 2); e.Property(x => x.MaximumMarks).HasPrecision(10, 2);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Invigilator).WithMany().HasForeignKey(x => x.InvigilatorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ExamInvigilator>(e => {
            e.HasIndex(x => new { x.ExamId, x.TeacherId }).IsUnique();
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdmissionQuestionDecision>(e => {
            e.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
            e.Property(x => x.DeductedMarks).HasPrecision(8, 2);
            e.HasOne(x => x.Attempt).WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EvidenceLog).WithMany().HasForeignKey(x => x.EvidenceLogId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdmissionAudit>().HasIndex(x => new { x.ExamId, x.AtUtc });
        b.Entity<Exam>().Property(x => x.ApplicationFee).HasPrecision(10, 2);
        b.Entity<ExamResult>().Property(x => x.ReviewDeduction).HasPrecision(8, 2);
    }
}
