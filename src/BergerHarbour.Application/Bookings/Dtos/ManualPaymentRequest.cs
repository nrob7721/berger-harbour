namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A manual payment recorded by staff.</summary>
public sealed record ManualPaymentRequest(
    decimal Amount,
    DateOnly Date,
    string? Note,
    int Version);
