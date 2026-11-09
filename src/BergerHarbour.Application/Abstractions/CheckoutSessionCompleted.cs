using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Abstractions;

/// <summary>A checkout session was paid (only synchronous methods are allowed, so completed means paid).</summary>
public sealed record CheckoutSessionCompleted(
    string SessionId,
    BookingId BookingId,
    CheckoutPurpose Purpose,
    string PaymentIntentId,
    decimal Amount,
    DateTimeOffset PaidAt)
    : PaymentWebhookEvent(SessionId, BookingId);
