using System.Net;
using BergerHarbour.Infrastructure.IntegrationTests.Support;
using BergerHarbour.Infrastructure.Web;

namespace BergerHarbour.Infrastructure.IntegrationTests.Web;

/// <summary>The public API's per-IP limiter, end to end through the real middleware pipeline.</summary>
public class RateLimitingMiddlewareTests
{
    private const string Secret = "edge-secret";

    private static ApiFactory<PublicApi.Controllers.BoatsController> Factory(string? limit, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [ConfigKeys.PublicBookingBaseUrl] = "https://bookings.test",
            [ConfigKeys.CorsAllowedOrigin] = "https://bookings.test",
            [ConfigKeys.EdgeProxySecret] = Secret,
            [ConfigKeys.FirestoreProjectId] = "demo-ratelimit",
            [ConfigKeys.StripeSecretKey] = "sk_test_x",
            [ConfigKeys.StripeWebhookSecret] = "whsec_x",
            [ConfigKeys.TurnstileSecret] = "turnstile",
            [ConfigKeys.PaymentLinkSecret] = "a-payment-link-secret-of-32-chars-or-more",
            [ConfigKeys.EmailMode] = "File",
        };
        if (limit is not null)
        {
            settings[ConfigKeys.RateLimitPerMinute] = limit;
        }

        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new ApiFactory<PublicApi.Controllers.BoatsController>("Testing", settings);
    }

    private static HttpRequestMessage Request(string ip, bool withSecret = true, string path = "/api/not-a-route", HttpMethod? method = null)
    {
        var m = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        m.Headers.Add(Headers.ConnectingIp, ip);
        if (withSecret)
        {
            m.Headers.Add(Headers.EdgeProxySecret, Secret);
        }

        return m;
    }

    [Fact]
    public async Task The_request_over_the_limit_gets_429_with_retry_after_and_other_ips_are_unaffected()
    {
        await using var factory = Factory("3");
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("1.1.1.1"))).StatusCode);
        }

        var limited = await client.SendAsync(Request("1.1.1.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Contains(RateLimitingMiddleware.TooManyRequests, await limited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("2.2.2.2"))).StatusCode);
    }

    [Fact]
    public async Task The_stripe_webhook_is_exempt()
    {
        await using var factory = Factory("1");
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var r = await client.SendAsync(Request("3.3.3.3", withSecret: false, path: "/api/stripe/webhook", method: HttpMethod.Post));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // a bad signature, never 429 or 403
        }
    }

    [Fact]
    public async Task Requests_without_the_proxy_secret_are_rejected_before_the_limiter_and_use_no_permits()
    {
        await using var factory = Factory("2");
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request("4.4.4.4", withSecret: false))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("4.4.4.4"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("4.4.4.4"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request("4.4.4.4"))).StatusCode);
    }

    [Fact]
    public async Task Zero_disables_the_limit()
    {
        await using var factory = Factory("0");
        var client = factory.CreateClient();
        for (var i = 0; i < 80; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("5.5.5.5"))).StatusCode);
        }
    }

    [Fact]
    public async Task The_default_is_60_per_minute()
    {
        await using var factory = Factory(null);
        var client = factory.CreateClient();
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("6.6.6.6"))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request("6.6.6.6"))).StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("lots")]
    public async Task Invalid_settings_stop_startup(string limit)
    {
        await using var factory = Factory(limit);
        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(ConfigKeys.RateLimitPerMinute, e.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dev_bypasses_are_refused_outside_local()
    {
        await using var factory = Factory("5", new Dictionary<string, string?> { [ConfigKeys.DevAdminEmail] = "me@example.com" });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task A_missing_cors_origin_stops_startup()
    {
        await using var factory = Factory("5", new Dictionary<string, string?> { [ConfigKeys.CorsAllowedOrigin] = "" });
        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(ConfigKeys.CorsAllowedOrigin, e.ToString(), StringComparison.Ordinal);
    }
}
