namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>An add-on definition, in the admin-defined order.</summary>
public sealed record AddonDto(
    string Id,
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
