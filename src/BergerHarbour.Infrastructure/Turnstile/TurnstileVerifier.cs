using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    /// <summary>Cloudflare's always-passes test secret; used only in Local when no secret is set.</summary>
    public const string TestSecret = "1x0000000000000000000000000000000AA";
    public string Secret { get; set; } = "";
}

public sealed class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options, ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        var form = new Dictionary<string, string> { ["secret"] = options.Value.Secret, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp)) form["remoteip"] = remoteIp;
        try
        {
            using var response = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            if (result?.Success != true) logger.LogInformation("Turnstile rejected a token: {Codes}", string.Join(",", result?.ErrorCodes ?? []));
            return result?.Success == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(e, "Turnstile verification failed");
            throw new Application.Common.ExternalServiceException("We couldn't check the security challenge. Please try again.", e);
        }
    }
}
