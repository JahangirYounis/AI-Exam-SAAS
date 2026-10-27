using System.Text.Encodings.Web;

namespace AiExamProctoring.Services.Web;

// Buffer only the exam document, so a Razor failure cannot leave a half-written
// blank page. API, video and streaming responses are not intercepted.
public sealed class SessionPageRecovery(RequestDelegate next, IWebHostEnvironment environment,
    ILogger<SessionPageRecovery> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var resultPage = context.Request.Path.StartsWithSegments("/Student/Result/Details") ||
            context.Request.Path.StartsWithSegments("/Student/Result/QuestionReview");
        if (!HttpMethods.IsGet(context.Request.Method) ||
            (!resultPage && !context.Request.Path.Equals(new PathString("/Student/Exam/Session"))))
        { await next(context); return; }
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            if ((context.Response.StatusCode == 200 || context.Response.StatusCode >= 500) && buffer.Length == 0)
                throw new InvalidOperationException($"Exam session returned an empty HTTP {context.Response.StatusCode} response.");
            buffer.Position = 0;
            context.Response.Body = original;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            logger.LogError(error, "Session page failed; reference {Reference}", context.TraceIdentifier);
            try
            {
                var directory = Path.Combine(PrivateStorage.Root(environment), "diagnostics");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, $"session-{Guid.NewGuid():N}.log"),
                    $"UTC: {DateTime.UtcNow:O}\nReference: {context.TraceIdentifier}\nSession: {context.Request.Query["sessionAttemptId"]}\n{error}");
            }
            catch (Exception diagnosticError) { logger.LogWarning(diagnosticError, "Session diagnostic could not be saved"); }
            context.Response.Body = original;
            context.Response.Clear();
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            var reference = HtmlEncoder.Default.Encode(context.TraceIdentifier);
            var retry = resultPage ? "/Student/Result" : int.TryParse(context.Request.Query["sessionAttemptId"], out var id) && id > 0
                ? $"/Student/Exam/Session?sessionAttemptId={id}" : "/Student/Exam/Available";
            var heading = resultPage ? "Your result could not open" : "Your session could not open";
            var message = resultPage ? "Your saved result has not been changed. Open My Results and try again, or contact your teacher." : "Your saved answers have not been reset. Retry opening the session, or contact your teacher.";
            var link = resultPage ? "My Results" : "Retry opening session";
            await context.Response.WriteAsync($"""
                <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{heading}</title><body style="font-family:system-ui;background:#f4f6fa;padding:32px">
                <main style="max-width:720px;margin:40px auto;background:white;padding:32px;border-radius:16px">
                <h1>{heading}</h1><p>{message}</p>
                <p><a href="{retry}">{link}</a> · <a href="/Student/Exam/Available">My examinations</a></p>
                <p>Support reference: {reference}</p></main></body></html>
                """, context.RequestAborted);
        }
        finally { context.Response.Body = original; }
    }
}
