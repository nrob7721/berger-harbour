namespace BergerHarbour.Application.Abstractions;

/// <summary>Cloudflare Turnstile bot protection.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
