using AiExamProctoring.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiExamProctoring.Services.Proctoring;

// Serialize answer/position writes with lock/resume transitions across app instances.
// SubmitSession owns its transaction and attempt lock, including concurrency retries.
// Wrapping it here would start a nested SQL transaction and reject every real MVC submission.
public sealed class ExamWriteGuard(ApplicationDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values["area"]?.ToString() != "Student" ||
            context.RouteData.Values["controller"]?.ToString() != "Exam" ||
            context.RouteData.Values["action"]?.ToString() is not ("SaveAnswer" or "SavePosition" or "ToggleReview"))
        { await next(); return; }
        var request = context.HttpContext.Request;
        if (!request.HasFormContentType) { context.Result = new StatusCodeResult(415); return; }
        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        if (!int.TryParse(form["sessionAttemptId"], out var sid) || sid <= 0)
        { context.Result = new BadRequestObjectResult(new { message = "A valid session attempt is required." }); return; }
        var studentId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var id = await db.ExamSessionAttempts.AsNoTracking().Where(s => s.SessionAttemptId == sid && s.Attempt!.StudentId == studentId)
            .Select(s => s.AttemptId).SingleOrDefaultAsync(request.HttpContext.RequestAborted);
        if (id <= 0) { context.Result = new NotFoundResult(); return; }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(request.HttpContext.RequestAborted);
            await ProctoringStateService.LockAttemptAsync(db, id, request.HttpContext.RequestAborted);
            var result = await next();
            if (result.Exception is null || result.ExceptionHandled) await tx.CommitAsync(request.HttpContext.RequestAborted);
        });
    }
}
