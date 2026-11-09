namespace BergerHarbour.Application.Abstractions;

/// <summary>All time access goes through this port so tests control time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
