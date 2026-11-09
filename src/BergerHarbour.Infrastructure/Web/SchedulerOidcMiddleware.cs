using Microsoft.AspNetCore.Http;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>The jobs endpoint isn't behind Access: it requires Cloud Scheduler's OIDC token (bypassed only in Local).</summary>
public sealed class SchedulerOidcMiddleware
{
    private readonly RequestDelegate next;
    private readonly bool bypass;

    public SchedulerOidcMiddleware(RequestDelegate next, bool bypass)
    {
        this.next = next;
        this.bypass = bypass;
    }

    public async Task InvokeAsync(HttpContext context, GoogleOidcTokenValidator validator)
    {
        if (!ApiPaths.IsJobs(context.Request.Path) || bypass)
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        if (!await validator.ValidateAsync(token))
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
            return;
        }

        await next(context);
    }
}
