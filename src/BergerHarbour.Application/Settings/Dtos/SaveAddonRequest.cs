namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>Create or edit an add-on (version 0 when creating).</summary>
public sealed record SaveAddonRequest(
    string Name,
    string? Description,
    bool PriceOnRequest,
    decimal? MidweekPrice,
    decimal? WeekendPrice,
    decimal? WeekPrice,
    bool QuantityApplies,
    int? Capacity,
    string? CapacityUnitLabel,
    bool IsActive,
    int Version);
