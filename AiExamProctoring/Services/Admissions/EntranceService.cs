using System.Data;
using System.Security.Cryptography;
using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Services.Admissions;

public sealed class EntranceService(ApplicationDbContext db, IDataProtectionProvider protection)
{
    private readonly PasswordHasher<UniversityCandidate> hasher = new();
    private readonly IDataProtector protector = protection.CreateProtector("SBBU.TemporaryExamPassword.v1");

    public string? TemporaryPassword(UniversityCandidate candidate) => candidate.MustChangePassword &&
        candidate.ProtectedTemporaryPassword is not null ? protector.Unprotect(candidate.ProtectedTemporaryPassword) : null;

    private static void Audit(ApplicationDbContext db, int exam, int? application, string actor, string action, string details) =>
        db.AdmissionAudits.Add(new() { ExamId = exam, ApplicationId = application, ActorId = actor, Action = action, Details = details });

    // Exam row is the shared lock order for admissions, allocation, adjudication and publication.
    public async Task WriteAsync(int examId, Func<Task> action, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT ExamId FROM Exams WITH (UPDLOCK, HOLDLOCK) WHERE ExamId = {examId}", ct);
            await action();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task ApproveAsync(int applicationId, string actor, bool approve, string note, CancellationToken ct)
    {
        var examId = await db.AdmissionApplications.Where(x => x.Id == applicationId).Select(x => x.ExamId).SingleAsync(ct);
        await WriteAsync(examId, async () => {
            var app = await db.AdmissionApplications.Include(x => x.Exam).Include(x => x.Student).SingleAsync(x => x.Id == applicationId, ct);
            if (app.Exam!.ResultsPublishedAtUtc is not null || app.Exam.Status == ExamStatus.Running || app.Exam.Status == ExamStatus.Completed)
                throw new InvalidOperationException("Registration decisions cannot change after the entry test starts.");
            if (app.Status == AdmissionStatus.Approved)
                throw new InvalidOperationException("This application is already approved. Its permanent roll number is retained.");
            app.VerificationNote = note; app.VerifiedBy = actor; app.VerifiedAtUtc = DateTime.UtcNow;
            if (!approve) {
                app.Status = AdmissionStatus.Rejected; app.PaymentStatus = AdmissionPaymentStatus.Rejected;
                Audit(db, examId, app.Id, actor, "ApplicationRejected", note); return;
            }
            if (app.PaymentStatus != AdmissionPaymentStatus.Submitted || app.PaidAmount != app.FeeDue ||
                string.IsNullOrEmpty(app.ReceiptFile) || string.IsNullOrEmpty(app.PaymentReference))
                throw new InvalidOperationException("Verify a submitted receipt and exact fee amount before approving.");
            if (app.Student is null || app.Student.IsBlocked || string.IsNullOrWhiteSpace(app.Student.CNIC) ||
                string.IsNullOrWhiteSpace(app.Student.ProfileImage) || string.IsNullOrWhiteSpace(app.Student.FatherName))
                throw new InvalidOperationException("The candidate profile needs CNIC, father's name and an enrolled photo.");
            var candidate = await db.UniversityCandidates.SingleOrDefaultAsync(x => x.StudentId == app.StudentId, ct);
            if (candidate is null) {
                candidate = new() { StudentId = app.StudentId, Campus = app.Campus };
                db.UniversityCandidates.Add(candidate);
                await db.SaveChangesAsync(ct);
                candidate.RollNumber = $"SBBU_{candidate.Campus}_{candidate.Id:D6}";
                var password = "Sb!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(9)) + "a";
                candidate.PasswordHash = hasher.HashPassword(candidate, password);
                candidate.ProtectedTemporaryPassword = protector.Protect(password);
            }
            if (candidate.Campus != app.Campus)
                throw new InvalidOperationException("This candidate already has a permanent roll number for a different campus. An administrator must review the campus choice.");
            var assignment = await db.ExamAssignments.SingleOrDefaultAsync(x => x.ExamId == examId && x.StudentId == app.StudentId, ct);
            if (assignment is null) {
                assignment = new() { ExamId = examId, StudentId = app.StudentId, AssignedBy = actor };
                db.ExamAssignments.Add(assignment);
            }
            assignment.RollNumber = candidate.RollNumber; assignment.IsActive = true;
            app.Status = AdmissionStatus.Approved; app.PaymentStatus = AdmissionPaymentStatus.Verified;
            app.Student.IsApproved = true;
            Audit(db, examId, app.Id, actor, "ApplicationApproved", "Payment and profile verified. Permanent roll issued. " + note);
        }, ct);
    }

    public async Task AllocateAsync(int examId, string actor, CancellationToken ct)
    {
        await WriteAsync(examId, async () => {
            var exam = await db.Exams.SingleAsync(x => x.ExamId == examId, ct);
            if (exam.ResultsPublishedAtUtc is not null) throw new InvalidOperationException("Published entry test is read only.");
            var teachers = await db.ExamInvigilators.Where(x => x.ExamId == examId && x.IsActive && x.CanMonitor && x.AcceptedAtUtc != null).OrderBy(x => x.TeacherId).ToListAsync(ct);
            var apps = await db.AdmissionApplications.Where(x => x.ExamId == examId && x.Status == AdmissionStatus.Approved).OrderBy(x => x.Id).ToListAsync(ct);
            var activeIds = teachers.Select(x => x.TeacherId).ToHashSet();
            var counts = teachers.ToDictionary(x => x.TeacherId, x => apps.Count(a => a.InvigilatorId == x.TeacherId));
            var pending = apps.Where(x => x.InvigilatorId is null || !activeIds.Contains(x.InvigilatorId)).ToList();
            if (teachers.Sum(x => Math.Max(0, x.Capacity - counts[x.TeacherId])) < pending.Count)
                throw new InvalidOperationException("Add more invigilators or increase their capacity (maximum 30) before allocating candidates.");
            foreach (var app in pending) {
                var teacher = teachers.Where(x => counts[x.TeacherId] < x.Capacity).OrderBy(x => counts[x.TeacherId]).ThenBy(x => x.TeacherId).First();
                app.InvigilatorId = teacher.TeacherId; counts[teacher.TeacherId]++;
            }
            Audit(db, examId, null, actor, "CandidatesAllocated", $"Allocated {pending.Count} candidates across {teachers.Count} active invigilators.");
        }, ct);
    }

    public async Task DecideAsync(int applicationId, int evidenceId, int questionId, bool confirmed, string reason, string teacher, CancellationToken ct)
    {
        var examId = await db.AdmissionApplications.Where(x => x.Id == applicationId).Select(x => x.ExamId).SingleAsync(ct);
        await WriteAsync(examId, async () => {
            var app = await db.AdmissionApplications.Include(x => x.Exam).SingleAsync(x => x.Id == applicationId, ct);
            if (!await EntranceAccess.CanReviewAsync(db, examId, app.StudentId, teacher, ct)) throw new UnauthorizedAccessException();
            if (app.Exam!.ResultsPublishedAtUtc is not null) throw new InvalidOperationException("Published results cannot be changed.");
            var log = await db.ProctoringLogs.SingleOrDefaultAsync(x => x.LogId == evidenceId && x.ExamId == examId && x.StudentId == app.StudentId, ct);
            if (log?.AttemptId is not int attemptId || log.QuestionIdAtCapture != questionId)
                throw new InvalidOperationException("This evidence does not have a recorded link to that question. Do not infer a question from a correct answer alone.");
            var attempt = await db.ExamAttempts.Include(x => x.Result).SingleAsync(x => x.AttemptId == attemptId, ct);
            if (attempt.Status is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked || attempt.Result is null)
                throw new InvalidOperationException("Wait until this candidate has submitted the full entry test.");
            var answer = await db.StudentAnswers.SingleOrDefaultAsync(x => x.SessionAttempt!.AttemptId == attemptId && x.QuestionId == questionId, ct);
            if (answer is null) throw new InvalidOperationException("No saved answer exists for this question.");
            var decision = await db.AdmissionQuestionDecisions.SingleOrDefaultAsync(x => x.AttemptId == attemptId && x.QuestionId == questionId, ct);
            if (decision is null) {
                decision = new() { AttemptId = attemptId, QuestionId = questionId };
                db.AdmissionQuestionDecisions.Add(decision);
            }
            var deduction = confirmed && answer.IsCorrect == true ? Math.Max(0, answer.MarksAwarded) : 0;
            Audit(db, examId, app.Id, teacher, "QuestionDecision", $"Attempt {attemptId}; question {questionId}; evidence {evidenceId}; confirmed {confirmed}; prior deduction {decision.DeductedMarks}; new deduction {deduction}. {reason}");
            decision.EvidenceLogId = evidenceId; decision.CheatingConfirmed = confirmed; decision.DeductedMarks = deduction;
            decision.Reason = reason; decision.ReviewerId = teacher; decision.ReviewedAtUtc = DateTime.UtcNow;
            log.IsReviewed = true; app.ReviewCompletedAtUtc = null; app.ReviewedBy = null;
            await db.SaveChangesAsync(ct);
            var totalDeduction = await db.AdmissionQuestionDecisions.Where(x => x.AttemptId == attemptId).SumAsync(x => x.DeductedMarks, ct);
            var raw = attempt.Result.TotalMarksObtained + attempt.Result.ReviewDeduction;
            attempt.Result.ReviewDeduction = totalDeduction;
            attempt.TotalMarksObtained = attempt.Result.TotalMarksObtained = raw - totalDeduction;
            attempt.Percentage = attempt.Result.Percentage = attempt.TotalMarks <= 0 ? 0 : Math.Round(attempt.TotalMarksObtained / attempt.TotalMarks * 100, 2);
            attempt.IsPassed = attempt.Result.IsPassed = !attempt.IsDisqualified && attempt.Percentage >= app.Exam.PassingPercentage;
        }, ct);
    }

    public async Task CompleteReviewAsync(int applicationId, string teacher, CancellationToken ct)
    {
        var examId = await db.AdmissionApplications.Where(x => x.Id == applicationId).Select(x => x.ExamId).SingleAsync(ct);
        await WriteAsync(examId, async () => {
            var app = await db.AdmissionApplications.Include(x => x.Exam).SingleAsync(x => x.Id == applicationId, ct);
            if (!await EntranceAccess.CanReviewAsync(db, examId, app.StudentId, teacher, ct)) throw new UnauthorizedAccessException();
            if (app.Exam!.ResultsPublishedAtUtc is not null) throw new InvalidOperationException("Results have already been published.");
            var attempts = await db.ExamAttempts.Where(x => x.ExamId == examId && x.StudentId == app.StudentId).ToListAsync(ct);
            if (attempts.Any(x => x.Status is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked or AttemptStatus.NotStarted))
                throw new InvalidOperationException("This candidate still has an unfinished or blocked attempt.");
            if (attempts.Count == 0 && DateTime.Now <= app.Exam.LatestStartDate)
                throw new InvalidOperationException("The entry window is still open. This candidate can still start.");
            if (await db.ProctoringLogs.AnyAsync(x => x.ExamId == examId && x.StudentId == app.StudentId && !x.IsReviewed &&
                (x.CountsAsViolation || x.ScreenshotPath != null || x.RecordingPath != null), ct))
                throw new InvalidOperationException("Review every saved violation/evidence item before completing candidate review.");
            app.ReviewedBy = teacher; app.ReviewCompletedAtUtc = DateTime.UtcNow;
            Audit(db, examId, app.Id, teacher, "CandidateReviewCompleted", attempts.Count == 0 ? "Absent candidate reviewed after entry window." : "Candidate evidence review completed.");
        }, ct);
    }

    public async Task PublishAsync(int examId, string actor, CancellationToken ct)
    {
        await WriteAsync(examId, async () => {
            var exam = await db.Exams.SingleAsync(x => x.ExamId == examId && x.Purpose == ExamPurpose.Entrance, ct);
            if (exam.ResultsPublishedAtUtc is not null) return;
            if (DateTime.Now <= exam.LatestStartDate) throw new InvalidOperationException("Wait until the entry window closes.");
            if (await db.ExamAttempts.AnyAsync(x => x.ExamId == examId && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.ProctoringBlocked || x.Status == AttemptStatus.NotStarted), ct))
                throw new InvalidOperationException("Some candidates have active or blocked attempts. Finish those attempts first.");
            var apps = await db.AdmissionApplications.Where(x => x.ExamId == examId && x.Status == AdmissionStatus.Approved).ToListAsync(ct);
            if (apps.Count == 0 || apps.Any(x => x.ReviewCompletedAtUtc is null))
                throw new InvalidOperationException("Every approved candidate, including absent candidates, must have a completed teacher review.");
            if (await db.ExamAttempts.AnyAsync(x => x.ExamId == examId && x.Status == AttemptStatus.Completed && x.Result == null, ct))
                throw new InvalidOperationException("A completed attempt is missing its calculated result.");
            exam.ResultsPublishedAtUtc = DateTime.UtcNow; exam.ResultsPublishedBy = actor; exam.AdmissionsOpen = false; exam.Status = ExamStatus.Completed;
            foreach (var app in apps) db.StudentNotifications.Add(new() { StudentId = app.StudentId, ExamId = examId,
                Title = "Entry test results published", Message = $"Reviewed results for {exam.ExamTitle} are now available in your admissions portal.", Type = "ResultPublished" });
            Audit(db, examId, null, actor, "ResultsPublished", $"Published reviewed merit list for {apps.Count} admitted candidates.");
        }, ct);
    }
}
