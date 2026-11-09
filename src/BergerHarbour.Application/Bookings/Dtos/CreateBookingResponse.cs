namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>The hold and its Embedded Checkout client secret.</summary>
public sealed record CreateBookingResponse(
    string BookingId,
    string ClientSecret,
    string SessionId);
