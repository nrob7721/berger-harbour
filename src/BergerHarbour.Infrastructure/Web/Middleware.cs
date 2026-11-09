using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

public static class Headers
{
    public const string EdgeProxySecret = "X-Edge-Proxy-Secret";
    public const string AccessJwt = "Cf-Access-Jwt-Assertion";
    public const string ConnectingIp = "CF-Connecting-IP";

    /// <summary>The client IP forwarded by the Pages Function, else the socket address.</summary>
    public static string ClientIp(HttpContext context) =>
        context.Request.Headers[ConnectingIp].FirstOrDefault() is { Length: > 0 } ip ? ip : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// Requires the shared secret the Cloudflare Pages Function proxy adds. Exempt paths (the Stripe webhook, the jobs endpoint)
/// authenticate themselves. With no secret configured the check is off, which startup only allows in Local.
/// </summary>
public sealed class EdgeProxySecretMiddleware(RequestDelegate next, string? secret, IReadOnlyList<string> exemptPaths, ILogger<EdgeProxySecretMiddleware> logger)
{
    private readonly byte[]? _secret = string.IsNullOrEmpty(secret) ? null : Encoding.UTF8.GetBytes(secret);

    public async Task InvokeAsync(HttpContext context)
    {
        if (_secret is null || exemptPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }
        var sent = Encoding.UTF8.GetBytes(context.Request.Headers[Headers.EdgeProxySecret].FirstOrDefault() ?? "");
        if (!CryptographicOperations.FixedTimeEquals(sent, _secret))
        {
            logger.LogWarning("Rejected a request without the edge proxy secret: {Path}", context.Request.Path);
            await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden.");
            return;
        }
        await next(context);
    }
}

/// <summary>
/// Per-IP sliding-window limit on /api/* (in memory, exact only on one instance). Runs straight after the proxy-secret check,
/// so rejected requests never use permits. 0 disables it.
/// </summary>
public sealed class RateLimitingMiddleware : IAsyncDisposable
{
    public const string TooManyRequests = "Too many requests. Please wait a minute and try again.";

    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<string> _exemptPaths;
    private readonly PartitionedRateLimiter<string>? _limiter;

    public RateLimitingMiddleware(RequestDelegate next, int permitsPerMinute, IReadOnlyList<string> exemptPaths)
    {
        _next = next;
        _exemptPaths = exemptPaths;
        if (permitsPerMinute > 0)
            _limiter = PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetSlidingWindowLimiter(ip, _ =>
                new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_limiter is null || !context.Request.Path.StartsWithSegments("/api") || _exemptPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
        {
            await _next(context);
            return;
        }
        using var lease = _limiter.AttemptAcquire(Headers.ClientIp(context));
        if (!lease.IsAcquired)
        {
            var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var r) ? Math.Max(1, (int)Math.Ceiling(r.TotalSeconds)) : 60;
            context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Problems.WriteAsync(context, StatusCodes.Status429TooManyRequests, TooManyRequests);
            return;
        }
        await _next(context);
    }

    public ValueTask DisposeAsync() => _limiter?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public static class Problems
{
    public static Task WriteAsync(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title }, (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json");
    }
}
