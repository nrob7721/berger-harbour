using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Web;

/// <summary>
/// Requires the X-Edge-Proxy-Secret header the Cloudflare Pages Function adds, so the Cloud Run URL cannot be used
/// directly. The Stripe webhook and the jobs endpoint have their own authentication and are exempt.
/// </summary>
public sealed class EdgeProxySecretMiddleware(RequestDelegate next, string secret, bool allowWhenUnset,
    IReadOnlyList<PathString> exemptPrefixes, ILogger<EdgeProxySecretMiddleware> logger)
{
    public const string HeaderName = "X-Edge-Proxy-Secret";

    private readonly byte[]? _expected = string.IsNullOrEmpty(secret) ? null : Encoding.UTF8.GetBytes(secret);

    public async Task InvokeAsync(HttpContext context)
    {
        if (exemptPrefixes.Any(p => context.Request.Path.StartsWithSegments(p)) || HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (_expected is null)
        {
            if (allowWhenUnset)
            {
                await next(context);
                return;
            }

            logger.LogError("EDGE_PROXY_SECRET is not configured; rejecting request");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var provided = Encoding.UTF8.GetBytes(context.Request.Headers[HeaderName].ToString());
        if (!CryptographicOperations.FixedTimeEquals(provided, _expected))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}
