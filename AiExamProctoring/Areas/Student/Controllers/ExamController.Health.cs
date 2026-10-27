using Microsoft.AspNetCore.Mvc;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> DeviceHealth(int attemptId,int sessionAttemptId,string issue) {
  if(StudentId is null)return Unauthorized();
  if(issue is not ("healthy" or "camera_disconnected" or "microphone_disconnected" or "camera_dark"))return BadRequest();
  var result=await _states.HealthAsync(attemptId,sessionAttemptId,StudentId,issue,HttpContext.RequestAborted);
  return result is null?NotFound():Json(new{state=result.State,warning=result.Warning,issue=result.Issue,warningCount=result.WarningCount,needsReview=result.NeedsReview});
 }
}
