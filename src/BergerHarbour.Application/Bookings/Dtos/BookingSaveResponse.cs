namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>A saved booking with staff's non-blocking warnings.</summary>
public sealed record BookingSaveResponse(
    AdminBookingDetail Booking,
    IReadOnlyList<string> Warnings);
