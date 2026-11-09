using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Infrastructure.IntegrationTests.Fakes;

public sealed class PassingTurnstileVerifier : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) => Task.FromResult(true);
}
