namespace BergerHarbour.Domain.Bookings;

/// <summary>A payment received for a booking.</summary>
public sealed record Payment(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    BookingCreator RecordedBy);
