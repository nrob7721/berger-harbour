namespace BergerHarbour.Application.Abstractions;

/// <summary>Verifies a Cloudflare Turnstile token server-side.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
