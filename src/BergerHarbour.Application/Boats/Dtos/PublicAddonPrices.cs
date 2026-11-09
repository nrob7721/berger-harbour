namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>An add-on's prices for the online period types (null when price on request).</summary>
public sealed record PublicAddonPrices(
    decimal? Midweek,
    decimal? Weekend,
    decimal? Week);
