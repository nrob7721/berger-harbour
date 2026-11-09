using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class FakeTurnstileVerifier : ITurnstileVerifier
{
    public bool Passes { get; set; } = true;

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) => Task.FromResult(Passes && token is not null);
}
