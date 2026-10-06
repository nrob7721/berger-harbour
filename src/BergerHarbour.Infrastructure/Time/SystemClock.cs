using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
