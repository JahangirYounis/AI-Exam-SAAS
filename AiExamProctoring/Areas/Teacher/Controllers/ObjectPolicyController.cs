using System.Security.Claims;
using System.Text.Json;
using AiExamProctoring.Data;
using AiExamProctoring.Services.Proctoring;
using AiExamProctoring.Services.ObjectDetection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Teacher.Controllers;
[Area("Teacher"),Authorize(Roles="Teacher,Admin")]
public sealed class ObjectPolicyController(ApplicationDbContext db,IObjectDetectionClient detector):Controller {
 private string? Actor=>User.FindFirstValue(ClaimTypes.NameIdentifier);
 [HttpGet] public async Task<IActionResult> Edit(int id,CancellationToken ct){
  var exam=await db.Exams.SingleOrDefaultAsync(e=>e.ExamId==id&&(e.CreatedBy==Actor||User.IsInRole("Admin")),ct);if(exam is null)return NotFound();
  var health=await detector.HealthAsync(ct);ViewBag.Supported=health.Data?.Classes??new List<string>();ViewBag.ExamId=id;ViewBag.ExamName=exam.ExamTitle;
  return View(ProctoringObjectPolicy.Read(exam.ObjectPolicyJson));
 }
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Edit(int id,List<string> prohibited,double confirmationSeconds,int confirmationSamples,double recoverySeconds,CancellationToken ct){
  if(prohibited.Any(x=>!ProctoringObjectPolicy.Names.ContainsKey(x))||!double.IsFinite(confirmationSeconds)||!double.IsFinite(recoverySeconds))return BadRequest();
  var exam=await db.Exams.SingleOrDefaultAsync(e=>e.ExamId==id&&(e.CreatedBy==Actor||User.IsInRole("Admin")),ct);if(exam is null)return NotFound();
  var policy=ProctoringObjectPolicy.Read(exam.ObjectPolicyJson);policy.Prohibited=prohibited.Distinct().ToList();policy.Version=Guid.NewGuid().ToString("N");policy.ConfirmationSeconds=Math.Clamp(confirmationSeconds,ProctoringObjectPolicy.MinimumConfirmationSeconds,10);policy.ConfirmationSamples=Math.Clamp(confirmationSamples,2,10);policy.RecoverySeconds=Math.Clamp(recoverySeconds,2,15);
  exam.ObjectPolicyJson=JsonSerializer.Serialize(policy);
  db.ProctoringLogs.Add(new(){ExamId=id,StudentId=Actor!,AlertType=AiExamProctoring.Models.ProctoringAlertType.SystemAudit,CountsAsViolation=false,PolicyProcessed=true,ActionByUserId=Actor,Description="Object policy updated. Version "+policy.Version,CreatedDate=DateTime.UtcNow,MachineObservationsJson=exam.ObjectPolicyJson,ModelName="Object policy audit"});
  await db.SaveChangesAsync(ct);return RedirectToAction(nameof(Edit),new{id});
 }
}
