namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>A blocked period.</summary>
public sealed record BlockedPeriodDto(
    string Id,
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight,
    bool UsesExtendedPaymentSchedule,
    int Version);
