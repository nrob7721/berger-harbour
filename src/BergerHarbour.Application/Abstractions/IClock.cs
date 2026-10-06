using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Abstractions;

/// <summary>All time access goes through this port so tests control time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public static class ClockExtensions
{
    /// <summary>Today in Australia/Sydney.</summary>
    public static DateOnly Today(this IClock clock) => SydneyTime.Today(clock.UtcNow);
}
