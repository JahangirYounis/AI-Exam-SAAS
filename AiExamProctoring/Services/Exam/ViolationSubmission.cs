using AiExamProctoring.Data;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Services.Exam;
public static class ViolationSubmission {
 // Caller owns the attempt lock and transaction. Grade saved answers; future parts
 // are skipped rather than reopening a session after terminal submission.
 public static async Task SubmitAsync(ApplicationDbContext db,ExamAttempt attempt,CancellationToken ct) {
  var now=DateTime.Now;
  var parts=await db.ExamSessions.Where(s=>s.ExamId==attempt.ExamId&&s.IsActive).OrderBy(s=>s.DisplayOrder).ToListAsync(ct);
  var existing=await db.ExamSessionAttempts.Include(s=>s.Session).Where(s=>s.AttemptId==attempt.AttemptId).ToListAsync(ct);
  foreach(var part in parts){var s=existing.SingleOrDefault(s=>s.SessionId==part.SessionId);
   if(s==null){s=new ExamSessionAttempt{Attempt=attempt,Session=part,StartedAt=now,Status=AttemptStatus.InProgress};db.ExamSessionAttempts.Add(s);await db.SaveChangesAsync(ct);}
   s.Attempt=attempt;
   if(s.Status is AttemptStatus.InProgress or AttemptStatus.ProctoringBlocked){await ExamLifecycleService.AutoSubmitSessionAsync(db,s,now,ct);s.RemainingTimeSeconds=0;}
  }
  attempt.SubmittedForViolations=true;
  await ExamLifecycleService.FinalizeAttemptAsync(db,attempt,now,ct);
  await db.SaveChangesAsync(ct);
 }
}
