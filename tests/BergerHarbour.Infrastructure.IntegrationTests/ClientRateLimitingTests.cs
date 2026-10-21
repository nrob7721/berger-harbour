using System.Net;
using BergerHarbour.Infrastructure.Web;
using BergerHarbour.PublicApi.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BergerHarbour.Infrastructure.IntegrationTests;

/// <summary>The per-IP limiter on public-api. No requests here reach Firestore (unknown routes and a rejected webhook).</summary>
public class ClientRateLimitingTests
{
    private const int Limit = 3;
    private const string Secret = "it-secret";

    private static WebApplicationFactory<Program> Factory(int? limit = Limit) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment(HostEnvironmentExtensions.Local);
        b.UseSetting("FIRESTORE_PROJECT_ID", "it-ratelimit");
        b.UseSetting("EDGE_PROXY_SECRET", Secret);
        b.UseSetting("EMAIL_MODE", "File");
        b.UseSetting("EMAIL_OUTPUT_DIR", Path.Combine(Path.GetTempPath(), "bh-it-emails"));
        if (limit is not null)
        {
            b.UseSetting(ClientRateLimitOptions.SettingName, limit.Value.ToString());
        }
    });

    private static HttpRequestMessage Get(string ip, string? secret = Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/no-such-route");
        request.Headers.Add("CF-Connecting-IP", ip);
        if (secret is not null)
        {
            request.Headers.Add(EdgeProxySecretMiddleware.HeaderName, secret);
        }

        return request;
    }

    [Fact]
    public async Task Requests_over_the_limit_get_429_with_Retry_After_and_a_friendly_title()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("203.0.113.1"))).StatusCode);
        }

        var rejected = await client.SendAsync(Get("203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(rejected.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);
        Assert.Contains(ClientRateLimitOptions.RejectedTitle, await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_different_client_ip_is_unaffected()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        for (var i = 0; i <= Limit; i++)
        {
            await client.SendAsync(Get("203.0.113.1"));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("203.0.113.2"))).StatusCode);
    }

    [Fact]
    public async Task The_stripe_webhook_is_never_limited()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < Limit * 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, StripeWebhookController.Route)
            {
                Content = new StringContent("{}"),
            };
            request.Headers.Add("CF-Connecting-IP", "203.0.113.1");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(request)).StatusCode);
        }
    }

    [Fact]
    public async Task Requests_without_the_edge_secret_are_rejected_before_the_limiter_and_use_no_permits()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < Limit * 3; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Get("203.0.113.1", secret: "wrong"))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("203.0.113.1"))).StatusCode);
    }

    [Fact]
    public async Task Zero_disables_the_limiter()
    {
        await using var factory = Factory(limit: 0);
        var client = factory.CreateClient();

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Get("203.0.113.1"))).StatusCode);
        }
    }

    [Theory]
    [InlineData("Production", null, 30)]
    [InlineData("Development", null, 30)]
    [InlineData("Local", null, 0)]
    [InlineData("Production", "12", 12)]
    [InlineData("Local", "5", 5)]
    public void Default_is_30_per_minute_except_in_Local_and_the_setting_overrides_it(string environment, string? setting, int expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        if (setting is not null)
        {
            builder.Configuration[ClientRateLimitOptions.SettingName] = setting;
        }

        builder.AddClientRateLimiting();

        using var provider = builder.Services.BuildServiceProvider();
        Assert.Equal(expected, provider.GetRequiredService<ClientRateLimitOptions>().PermitsPerMinute);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void An_invalid_setting_stops_startup(string setting)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[ClientRateLimitOptions.SettingName] = setting;

        Assert.Throws<InvalidOperationException>(() => builder.AddClientRateLimiting());
    }
}
