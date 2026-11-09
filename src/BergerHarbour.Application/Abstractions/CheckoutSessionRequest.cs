using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Abstractions;

/// <summary>An Embedded Checkout session for one amount of one booking.</summary>
public sealed record CheckoutSessionRequest(
    BookingId BookingId,
    CheckoutPurpose Purpose,
    decimal Amount,
    string ProductName,
    string Description,
    string CustomerEmail,
    string ReturnUrl,
    DateTimeOffset? ExpiresAt);
