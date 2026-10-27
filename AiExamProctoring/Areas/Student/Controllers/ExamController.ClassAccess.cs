using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
 public override async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next) {
  if(context.ActionDescriptor.RouteValues.TryGetValue("action",out var action) && action is "Evidence" or "AuditEvidence" or "Blocked") { await next(); return; }
  int? examId=null;
  if(context.ActionArguments.TryGetValue("examId",out var e)&&e is int eid)examId=eid;
  else if(context.ActionArguments.TryGetValue("sessionAttemptId",out var s)&&s is int sid)examId=await _context.ExamSessionAttempts.Where(a=>a.SessionAttemptId==sid&&a.Attempt!.StudentId==StudentId).Select(a=>(int?)a.Attempt!.ExamId).FirstOrDefaultAsync();
  else if(context.ActionArguments.TryGetValue("attemptId",out var a)&&a is int aid)examId=await _context.ExamAttempts.Where(x=>x.AttemptId==aid&&x.StudentId==StudentId).Select(x=>(int?)x.ExamId).FirstOrDefaultAsync();
  if(examId.HasValue){var classId=await _context.Exams.Where(x=>x.ExamId==examId).Select(x=>x.AcademicClassId).FirstOrDefaultAsync();
   if(classId.HasValue&&!await _context.ClassMemberships.AnyAsync(m=>m.AcademicClassId==classId&&m.StudentId==StudentId&&m.IsActive&&m.AcademicClass!.IsActive)){context.Result=new ForbidResult();return;}}
  await next();
 }
}
