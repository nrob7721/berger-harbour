namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>The hold, its Embedded Checkout client secret and when Checkout closes (before the hold lapses).</summary>
public sealed record CreateBookingResponse(
    string BookingId,
    string ClientSecret,
    string SessionId,
    DateTimeOffset CheckoutExpiresAt);
