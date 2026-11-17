namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>POST /api/bookings/{id}/confirmation</c>: the manual payment that confirms an Expired hold (§7.3).</summary>
public sealed record ManualPaymentRequest(
    decimal Amount,
    DateOnly Date,
    string? Note,
    int Version);
