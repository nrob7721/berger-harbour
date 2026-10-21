using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>RATE_LIMIT_PER_MINUTE: requests per client IP per minute. 0 disables the limiter.</summary>
public sealed record ClientRateLimitOptions(int PermitsPerMinute, IReadOnlyList<PathString> ExemptPrefixes)
{
    public const int DefaultPermitsPerMinute = 30;
    public const string SettingName = "RATE_LIMIT_PER_MINUTE";
    public const string RejectedTitle = "Too many requests. Please wait a minute and try again.";
}

/// <summary>
/// Per-IP sliding-window limiter for the public API, replacing a Cloudflare WAF rule (the booking subdomain is not in
/// our Cloudflare zone). State is in memory, so it is only exact while the service runs a single instance (public-api
/// is deployed with max-instances 1). Idle IP partitions are dropped by the framework once their window has passed.
/// </summary>
public static class ClientRateLimiting
{
    private const int SegmentsPerWindow = 6;

    /// <summary>Default 30/min; off in Local unless RATE_LIMIT_PER_MINUTE is set explicitly (tests do).</summary>
    public static WebApplicationBuilder AddClientRateLimiting(this WebApplicationBuilder builder, params string[] exemptPrefixes)
    {
        var configured = builder.Configuration[ClientRateLimitOptions.SettingName];
        var permits = string.IsNullOrWhiteSpace(configured)
            ? builder.Environment.IsLocal() ? 0 : ClientRateLimitOptions.DefaultPermitsPerMinute
            : int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"{ClientRateLimitOptions.SettingName} must be a whole number of requests per minute (0 disables).");

        builder.Services.AddSingleton(new ClientRateLimitOptions(permits, exemptPrefixes.Select(p => new PathString(p)).ToList()));
        return builder;
    }

    /// <summary>Call after <see cref="WebDefaults.UseBergerHarbourWeb"/> so requests without the proxy secret never reach it.</summary>
    public static WebApplication UseClientRateLimiting(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ClientRateLimitOptions>();
        if (options.PermitsPerMinute > 0)
        {
            app.UseMiddleware<ClientRateLimitMiddleware>(CreateLimiter(options));
        }

        return app;
    }

    public static PartitionedRateLimiter<HttpContext> CreateLimiter(ClientRateLimitOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") ||
                HttpMethods.IsOptions(context.Request.Method) ||
                options.ExemptPrefixes.Any(p => context.Request.Path.StartsWithSegments(p)))
            {
                return RateLimitPartition.GetNoLimiter("exempt");
            }

            return RateLimitPartition.GetSlidingWindowLimiter(context.ClientIp() ?? "unknown", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = options.PermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
            });
        });
}

/// <summary>Answers 429 with Retry-After and a ProblemDetails title the booking app shows as-is.</summary>
public sealed class ClientRateLimitMiddleware(RequestDelegate next, PartitionedRateLimiter<HttpContext> limiter,
    IProblemDetailsService problemDetails)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var lease = await limiter.AcquireAsync(context, 1, context.RequestAborted);
        if (lease.IsAcquired)
        {
            await next(context);
            return;
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? (int)Math.Ceiling(delay.TotalSeconds) : 60;
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Max(retryAfter, 1).ToString(CultureInfo.InvariantCulture);
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = ClientRateLimitOptions.RejectedTitle,
            },
        });
    }
}
