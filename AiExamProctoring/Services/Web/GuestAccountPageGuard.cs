namespace AiExamProctoring.Services.Web;
public sealed class GuestAccountPageGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
    var path = context.Request.Path.Value?.TrimEnd('/');
    if (string.Equals(path, "/Identity/Account/Login", StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, "/Identity/Account/Register", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var area = context.User.IsInRole("Admin") ? "Admin" : context.User.IsInRole("Teacher") ? "Teacher" : "Student";
            context.Response.Redirect($"/{area}/Dashboard");
            return;
        }
    }
    await next(context);
    }
}
