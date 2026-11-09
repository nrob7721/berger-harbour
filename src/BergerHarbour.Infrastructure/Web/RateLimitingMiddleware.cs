using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// The public API's per-IP limiter (§8.1): a sliding window of N requests per minute per <c>CF-Connecting-IP</c> on
/// <c>/api/*</c>, in memory (exact on one instance). The Stripe webhook is exempt. <c>0</c> disables it.
/// </summary>
public sealed class RateLimitingMiddleware : IDisposable
{
    public const string TooManyRequests = "Too many requests. Please wait a minute and try again.";

    private readonly RequestDelegate next;
    private readonly PartitionedRateLimiter<string>? limiter;

    public RateLimitingMiddleware(RequestDelegate next, int permitsPerMinute)
    {
        this.next = next;
        limiter = permitsPerMinute <= 0
            ? null
            : PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
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
        if (limiter is null || !ApiPaths.IsApi(context.Request.Path) || ApiPaths.IsStripeWebhook(context.Request.Path))
        {
            await next(context);
            return;
        }

        using var lease = await limiter.AcquireAsync(ApiPaths.ClientIp(context) ?? "unknown", 1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await ProblemResponses.WriteAsync(context, StatusCodes.Status429TooManyRequests, TooManyRequests);
            return;
        }

        await next(context);
    }

    public void Dispose() => limiter?.Dispose();
}
