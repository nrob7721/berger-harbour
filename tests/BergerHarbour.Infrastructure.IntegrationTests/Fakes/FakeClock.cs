using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Infrastructure.IntegrationTests.Fakes;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
