using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}
