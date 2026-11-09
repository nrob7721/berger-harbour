namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>An add-on line on a booking.</summary>
public sealed record AddonLineDto(
    string Id,
    string AddonId,
    string Name,
    int Quantity,
    decimal? UnitPrice,
    decimal? LineTotal);
