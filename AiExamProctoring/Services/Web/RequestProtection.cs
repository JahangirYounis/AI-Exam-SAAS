using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AiExamProctoring.Services.Web;

public static class RequestProtection
{
    public static IServiceCollection AddRequestProtection(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Limit account POSTs separately: shared classroom IPs must not
                // throttle authenticated candidates who are saving answers.
                if (HttpMethods.IsPost(context.Request.Method) &&
                    context.Request.Path.StartsWithSegments("/Identity/Account"))
                {
                    var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter("account:" + address, _ => new()
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
                }

                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is not null)
                    return RateLimitPartition.GetTokenBucketLimiter("user:" + userId, _ => new()
                    {
                        TokenLimit = 360,
                        TokensPerPeriod = 60,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });

                return RateLimitPartition.GetNoLimiter("public");
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "10";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    ok = false,
                    message = "Too many requests. Please wait a moment and try again."
                }, cancellationToken);
            };
        });
        return services;
    }

    public static IApplicationBuilder UseResponseProtection(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
                headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=(), payment=(), usb=()";
                if (context.User.Identity?.IsAuthenticated == true ||
                    context.Request.Path.StartsWithSegments("/Identity"))
                {
                    headers.CacheControl = "no-store";
                    headers.Pragma = "no-cache";
                }
                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}
