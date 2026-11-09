namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A labelled blocked-period band across all rows.</summary>
public sealed record TimelineBlockedPeriod(
    string Id,
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight);
