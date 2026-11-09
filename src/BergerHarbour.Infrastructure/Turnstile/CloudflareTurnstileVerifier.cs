using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Turnstile;

/// <summary>Verifies a Turnstile token with Cloudflare's siteverify endpoint.</summary>
public sealed class CloudflareTurnstileVerifier : ITurnstileVerifier
{
    private readonly HttpClient http;
    private readonly TurnstileOptions options;
    private readonly ILogger<CloudflareTurnstileVerifier> logger;

    public CloudflareTurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<CloudflareTurnstileVerifier> logger)
    {
        this.http = http;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = options.Secret, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await http.PostAsync(options.VerifyUrl, new FormUrlEncodedContent(form), ct);
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            return result?.Success == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(e, "Turnstile verification failed.");
            return false;
        }
    }

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
