using BergerHarbour.Infrastructure.Cloudflare;
using BergerHarbour.Infrastructure.Google;
using BergerHarbour.Infrastructure.Web;

namespace BergerHarbour.AdminApi;

/// <summary>
/// Staff routes (everything but the jobs endpoint): a valid Cloudflare Access JWT whose email is allow-listed.
/// In Local only, DEV_ADMIN_EMAIL stands in for it.
/// </summary>
public sealed class AccessAuthenticationMiddleware(RequestDelegate next, AccessJwtValidator validator, IConfiguration config, IHostEnvironment env)
{
    public const string StaffEmailItem = "staffEmail";
    private readonly string? _devEmail = env.IsLocal() ? config[ConfigKeys.DevAdminEmail] : null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments(JobsAuthentication.Path))
        {
            await next(context);
            return;
        }
        var email = !string.IsNullOrWhiteSpace(_devEmail)
            ? _devEmail
            : await validator.ValidateAsync(context.Request.Headers[Headers.AccessJwt].FirstOrDefault(), context.RequestAborted);
        if (email is null)
        {
            await Problems.WriteAsync(context, StatusCodes.Status401Unauthorized, "Please sign in again.");
            return;
        }
        context.Items[StaffEmailItem] = email;
        await next(context);
    }
}

/// <summary>The jobs endpoint isn't behind Access: it requires Cloud Scheduler's Google OIDC token (or DEV_JOBS_BYPASS in Local).</summary>
public sealed class JobsAuthentication(SchedulerOidcValidator validator, IConfiguration config, IHostEnvironment env)
{
    public const string Path = "/api/jobs";

    public async Task<bool> IsAuthorisedAsync(HttpContext context)
    {
        if (env.IsLocal() && bool.TryParse(config[ConfigKeys.DevJobsBypass], out var bypass) && bypass) return true;
        var header = context.Request.Headers.Authorization.FirstOrDefault() ?? "";
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && await validator.ValidateAsync(header["Bearer ".Length..].Trim());
    }
}
