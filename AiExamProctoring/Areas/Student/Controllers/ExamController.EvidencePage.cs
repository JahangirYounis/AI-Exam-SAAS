using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
 [HttpGet] public async Task<IActionResult> Evidence(int? attemptId) {
  if(StudentId==null)return Challenge();
  ViewBag.Attempts=await _context.ExamAttempts.AsNoTracking().Include(a=>a.Exam).Where(a=>a.StudentId==StudentId).OrderByDescending(a=>a.AttemptId).ToListAsync();
  var logs=await _context.ProctoringLogs.AsNoTracking().Where(l=>l.StudentId==StudentId&&l.AttemptId!=null&&(!attemptId.HasValue||l.AttemptId==attemptId)&&l.Attempt!.StudentId==StudentId).OrderByDescending(l=>l.LogId).Take(500).ToListAsync();
  return View("Evidence",logs);
 }
}
