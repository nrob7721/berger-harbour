using System.Net;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BergerHarbour.Infrastructure.IntegrationTests;

/// <summary>The public API's per-IP limiter, end to end through the real middleware pipeline.</summary>
public class RateLimiterTests
{
    private const string Secret = "edge-secret";

    private static WebApplicationFactory<Program> Factory(string? limit) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing");
        b.UseSetting(ConfigKeys.PublicBookingBaseUrl, "https://bookings.test");
        b.UseSetting(ConfigKeys.CorsAllowedOrigin, "https://bookings.test");
        b.UseSetting(ConfigKeys.EdgeProxySecret, Secret);
        b.UseSetting(ConfigKeys.FirestoreProjectId, "demo-ratelimit");
        b.UseSetting(ConfigKeys.StripeSecretKey, "sk_test_x");
        b.UseSetting(ConfigKeys.StripeWebhookSecret, "whsec_x");
        b.UseSetting(ConfigKeys.TurnstileSecret, "turnstile");
        b.UseSetting(ConfigKeys.PaymentLinkSecret, "a-payment-link-secret-of-32-chars-or-more");
        b.UseSetting(ConfigKeys.EmailMode, "File");
        if (limit is not null) b.UseSetting(ConfigKeys.RateLimitPerMinute, limit);
    });

    private static HttpRequestMessage Request(string ip, bool withSecret = true, string path = "/api/not-a-route", HttpMethod? method = null)
    {
        var m = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        m.Headers.Add(Headers.ConnectingIp, ip);
        if (withSecret) m.Headers.Add(Headers.EdgeProxySecret, Secret);
        return m;
    }

    [Fact]
    public async Task Request_over_the_limit_gets_429_with_retry_after_and_other_ips_are_unaffected()
    {
        await using var factory = Factory("3");
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("1.1.1.1"))).StatusCode);
        var limited = await client.SendAsync(Request("1.1.1.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Contains(RateLimitingMiddleware.TooManyRequests, await limited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("2.2.2.2"))).StatusCode);
    }

    [Fact]
    public async Task Stripe_webhook_is_exempt()
    {
        await using var factory = Factory("1");
        var client = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var r = await client.SendAsync(Request("3.3.3.3", withSecret: false, path: "/api/stripe/webhook", method: HttpMethod.Post));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); // bad signature, never 429 or 403
        }
    }

    [Fact]
    public async Task Requests_without_the_proxy_secret_are_rejected_before_the_limiter_and_use_no_permits()
    {
        await using var factory = Factory("2");
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request("4.4.4.4", withSecret: false))).StatusCode);
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
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("5.5.5.5"))).StatusCode);
    }

    [Fact]
    public async Task Default_limit_is_60_per_minute()
    {
        await using var factory = Factory(null);
        var client = factory.CreateClient();
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("6.6.6.6"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request("6.6.6.6"))).StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("lots")]
    public async Task Invalid_settings_stop_startup(string limit)
    {
        await using var factory = Factory(limit);
        var e = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(ConfigKeys.RateLimitPerMinute, e.ToString());
    }

    [Fact]
    public async Task Dev_bypasses_are_refused_outside_local()
    {
        await using var factory = Factory("5").WithWebHostBuilder(b => b.UseSetting(ConfigKeys.DevAdminEmail, "me@example.com"));
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
