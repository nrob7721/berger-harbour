using System.Net;
using System.Net.Http.Json;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BergerHarbour.Infrastructure.IntegrationTests;

/// <summary>The required end-to-end race: two concurrent POST /api/bookings for the same boat and dates.</summary>
public class PublicApiRaceTests : IClassFixture<EmulatorFixture>
{
    private readonly EmulatorFixture _fx;

    public PublicApiRaceTests(EmulatorFixture fx) => _fx = fx;

    private sealed class FakeGateway : IPaymentGateway
    {
        public Task<CheckoutSessionResult> CreateEmbeddedCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default)
        {
            var id = "cs_test_" + Guid.NewGuid().ToString("N");
            return Task.FromResult(new CheckoutSessionResult(id, id + "_secret_x"));
        }
    }

    private sealed class AcceptAll : ITurnstileVerifier
    {
        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) => Task.FromResult(true);
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment(HostEnvironmentExtensions.Local);
        b.UseSetting("FIRESTORE_PROJECT_ID", _fx.ProjectId);
        b.UseSetting("EDGE_PROXY_SECRET", "it-secret");
        b.UseSetting("EMAIL_MODE", "File");
        b.UseSetting("EMAIL_OUTPUT_DIR", Path.Combine(Path.GetTempPath(), "bh-it-emails"));
        b.ConfigureServices(s =>
        {
            s.RemoveAll<IPaymentGateway>();
            s.AddSingleton<IPaymentGateway, FakeGateway>();
            s.RemoveAll<ITurnstileVerifier>();
            s.AddSingleton<ITurnstileVerifier, AcceptAll>();
        });
    });

    [Fact]
    public async Task Two_concurrent_requests_for_the_same_dates_one_wins_and_one_gets_409()
    {
        await using var factory = Factory();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SeedService>()
                .RunAsync(new SeedService.SeedOptions("staff@example.test", "02 4444 4444", "info@example.test"));
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Edge-Proxy-Secret", "it-secret");
        var start = NextMonday(DateTime.UtcNow.AddDays(45));

        async Task<string> DraftAsync(string email)
        {
            var response = await client.PostAsJsonAsync("/api/booking-drafts", new
            {
                slug = "pacific-blue",
                periodType = "Midweek",
                startDate = start.ToString("yyyy-MM-dd"),
                fullName = "Racer",
                email,
                mobile = "+61412345678",
                turnstileToken = "token",
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<DraftCreated>())!.DraftId;
        }

        object Body(string draftId, string email) => new
        {
            draftId,
            slug = "pacific-blue",
            periodType = "Midweek",
            startDate = start.ToString("yyyy-MM-dd"),
            numberOfAdults = 3,
            numberOfChildren = 1,
            fullName = "Racer",
            email,
            mobile = "+61412345678",
            addons = Array.Empty<object>(),
            roomingWarningAccepted = false,
            termsAccepted = true,
        };

        var drafts = await Task.WhenAll(DraftAsync("a@example.com"), DraftAsync("b@example.com"));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/bookings", Body(drafts[0], "a@example.com")),
            client.PostAsJsonAsync("/api/bookings", Body(drafts[1], "b@example.com")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("Those dates are no longer available", await loser.Content.ReadAsStringAsync());

        var availability = await client.GetStringAsync(
            $"/api/boats/pacific-blue/availability?from={start.AddDays(-1):yyyy-MM-dd}&to={start.AddDays(10):yyyy-MM-dd}");
        Assert.Contains(start.ToString("yyyy-MM-dd"), availability);
    }

    [Fact]
    public async Task Requests_without_the_proxy_secret_are_rejected()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().GetAsync("/api/boats/pacific-blue");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record DraftCreated(string DraftId);

    private static DateOnly NextMonday(DateTime from)
    {
        var d = DateOnly.FromDateTime(from);
        while (d.DayOfWeek != DayOfWeek.Monday)
        {
            d = d.AddDays(1);
        }

        return d;
    }
}
