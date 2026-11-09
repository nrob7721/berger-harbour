namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>Create or edit a blocked period (version 0 when creating).</summary>
public sealed record SaveBlockedPeriodRequest(
    string Name,
    DateOnly FirstNight,
    DateOnly LastNight,
    bool UsesExtendedPaymentSchedule,
    int Version);
