using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Abstractions;

/// <summary>A checkout session expired without being paid.</summary>
public sealed record CheckoutSessionExpired(string SessionId, BookingId BookingId) : PaymentWebhookEvent(SessionId, BookingId);
