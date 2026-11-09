using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Abstractions;

/// <summary>A verified payment provider event about a checkout session.</summary>
public abstract record PaymentWebhookEvent(string SessionId, BookingId BookingId);
