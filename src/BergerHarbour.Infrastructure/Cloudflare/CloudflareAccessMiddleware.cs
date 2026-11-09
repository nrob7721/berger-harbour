using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Http;

namespace BergerHarbour.Infrastructure.Cloudflare;

/// <summary>
/// Every admin route except the jobs endpoint requires a valid Access JWT whose email is allowed. In Local only,
/// <c>DEV_ADMIN_EMAIL</c> stands in for it.
/// </summary>
public sealed class CloudflareAccessMiddleware
{
    public const string StaffEmailItem = "staffEmail";

    private readonly RequestDelegate next;
    private readonly string? devAdminEmail;

    public CloudflareAccessMiddleware(RequestDelegate next, string? devAdminEmail)
    {
        this.next = next;
        this.devAdminEmail = string.IsNullOrWhiteSpace(devAdminEmail) ? null : devAdminEmail;
    }

    public async Task InvokeAsync(HttpContext context, CloudflareAccessJwtValidator validator)
    {
        if (!ApiPaths.IsApi(context.Request.Path) || ApiPaths.IsJobs(context.Request.Path))
        {
            await next(context);
            return;
        }

        var email = devAdminEmail ?? await validator.ValidateAsync(context.Request.Headers[Headers.AccessJwt].FirstOrDefault(), context.RequestAborted);
        if (email is null)
        {
            await ProblemResponses.WriteAsync(context, StatusCodes.Status401Unauthorized, "Please sign in again.");
            return;
        }

        context.Items[StaffEmailItem] = email;
        await next(context);
    }
}
