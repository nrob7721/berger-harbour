using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    /// <summary>TURNSTILE_SECRET. Cloudflare's test secret 1x0000000000000000000000000000000AA always passes.</summary>
    public string SecretKey { get; set; } = string.Empty;
}

/// <summary>Verifies Turnstile tokens with Cloudflare's siteverify endpoint.</summary>
public sealed class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
    : ITurnstileVerifier
{
    public const string BaseAddress = "https://challenges.cloudflare.com/";

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = options.Value.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        using var response = await http.PostAsync("turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Turnstile siteverify returned {Status}", response.StatusCode);
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
        if (result?.Success != true)
        {
            logger.LogInformation("Turnstile rejected a token: {Errors}", string.Join(",", result?.ErrorCodes ?? []));
        }

        return result?.Success == true;
    }

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
