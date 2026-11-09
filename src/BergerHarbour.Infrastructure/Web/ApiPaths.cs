using Microsoft.AspNetCore.Http;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>Routes with their own authentication, exempt from the proxy secret.</summary>
public static class ApiPaths
{
    public const string StripeWebhook = "/api/stripe/webhook";
    public const string Jobs = "/api/jobs";

    public static bool IsApi(PathString path) => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    public static bool IsStripeWebhook(PathString path) => path.StartsWithSegments(StripeWebhook, StringComparison.OrdinalIgnoreCase);

    public static bool IsJobs(PathString path) => path.StartsWithSegments(Jobs, StringComparison.OrdinalIgnoreCase);

    public static string? ClientIp(HttpContext context) =>
        context.Request.Headers[Headers.ConnectingIp].FirstOrDefault() is { Length: > 0 } ip ? ip : context.Connection.RemoteIpAddress?.ToString();
}
