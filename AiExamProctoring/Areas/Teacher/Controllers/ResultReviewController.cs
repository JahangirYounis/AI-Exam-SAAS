using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.Proctoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher")]
public sealed class ResultReviewController(ApplicationDbContext db):Controller {
 string TeacherId=>User.FindFirstValue(ClaimTypes.NameIdentifier)!;
 public async Task<IActionResult> Index(int examId){if(!await db.Exams.AnyAsync(e=>e.ExamId==examId&&e.CreatedBy==TeacherId))return NotFound();ViewBag.ExamId=examId;return View(await db.ExamAttempts.AsNoTracking().Include(a=>a.Student).Include(a=>a.Result).Where(a=>a.ExamId==examId).OrderByDescending(a=>a.TotalMarksObtained).ToListAsync());}
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> ApproveAll(int examId){
  if(!await db.Exams.AnyAsync(e=>e.ExamId==examId&&e.CreatedBy==TeacherId))return NotFound();
  var ids=await db.ExamAttempts.Where(a=>a.ExamId==examId&&(a.Status==AttemptStatus.Completed||a.Status==AttemptStatus.AutoSubmitted)&&!a.IsDisqualified).Select(a=>a.AttemptId).ToListAsync();
  foreach(var id in ids)await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{await using var tx=await db.Database.BeginTransactionAsync();await ProctoringStateService.LockAttemptAsync(db,id,default);var a=await db.ExamAttempts.SingleAsync(a=>a.AttemptId==id);if(a.Status is AttemptStatus.Completed or AttemptStatus.AutoSubmitted){a.ResultReleasedAtUtc=DateTime.UtcNow;a.ResultReviewedBy=TeacherId;await db.SaveChangesAsync();}await tx.CommitAsync();});
  TempData["Success"]="Completed eligible results approved. Blocked, active and disqualified attempts remain unchanged.";return RedirectToAction(nameof(Index),new{examId});
 }
}
