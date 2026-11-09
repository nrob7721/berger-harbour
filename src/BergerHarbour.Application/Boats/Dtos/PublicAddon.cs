namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>An add-on offered for a boat, in the admin-defined order.</summary>
public sealed record PublicAddon(
    string Id,
    string Name,
    string? Description,
    bool PriceOnRequest,
    bool QuantityApplies,
    PublicAddonPrices Prices,
    bool HasCapacity,
    string? CapacityUnitLabel);
