namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A requested add-on with its price (null: price on request).</summary>
public sealed record QuoteAddonLine(
    string AddonId,
    string Name,
    int Quantity,
    decimal? UnitPrice,
    decimal? LineTotal);
