using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A payment received.</summary>
public sealed record PaymentDto(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    DateTimeOffset PaidAt,
    string? Note,
    BookingCreator RecordedBy);
