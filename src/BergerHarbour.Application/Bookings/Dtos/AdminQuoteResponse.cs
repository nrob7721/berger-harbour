namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>Rate-table prices; null where staff must enter the price.</summary>
public sealed record AdminQuoteResponse(
    decimal? HirePrice,
    IReadOnlyList<AddonPriceDto> AddonPrices);
