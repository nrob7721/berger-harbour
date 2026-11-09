namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>An add-on's unit price for the booking (null: staff enter it).</summary>
public sealed record AddonPriceDto(
    string AddonId,
    decimal? UnitPrice);
