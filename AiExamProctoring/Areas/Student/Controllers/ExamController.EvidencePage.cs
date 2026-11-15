using Microsoft.AspNetCore.Mvc;
namespace AiExamProctoring.Areas.Student.Controllers;
public partial class ExamController {
 [HttpGet] public IActionResult Evidence(int? attemptId) => StudentId == null ? Challenge() : RedirectToAction("Index", "SharedEvidence", new { area = "" });
}
