using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Requires the Pages Function proxy's <c>X-Edge-Proxy-Secret</c> on every route except the Stripe webhook and the jobs
/// endpoint. Bypassed only in Local when no secret is configured.
/// </summary>
public sealed class EdgeProxySecretMiddleware
{
    private readonly RequestDelegate next;
    private readonly byte[]? secret;
    private readonly ILogger<EdgeProxySecretMiddleware> logger;

    public EdgeProxySecretMiddleware(RequestDelegate next, string? secret, ILogger<EdgeProxySecretMiddleware> logger)
    {
        this.next = next;
        this.secret = string.IsNullOrEmpty(secret) ? null : Encoding.UTF8.GetBytes(secret);
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (secret is null || ApiPaths.IsStripeWebhook(context.Request.Path) || ApiPaths.IsJobs(context.Request.Path))
        {
            await next(context);
            return;
        }

        var sent = Encoding.UTF8.GetBytes(context.Request.Headers[Headers.EdgeProxySecret].FirstOrDefault() ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(sent, secret))
        {
            logger.LogWarning("Rejected a request without the edge proxy secret: {Path}", context.Request.Path.Value);
            await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden.");
            return;
        }

        await next(context);
    }
}
