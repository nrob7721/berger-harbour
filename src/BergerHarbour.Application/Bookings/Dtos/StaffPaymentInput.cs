namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>
/// A booking's payment as staff save it (§8.2): no id means a new manual payment; a payment with an id is kept as it is,
/// and its other fields are ignored.
/// </summary>
public sealed record StaffPaymentInput(
    string? Id,
    decimal Amount,
    DateOnly Date,
    string? Note);
