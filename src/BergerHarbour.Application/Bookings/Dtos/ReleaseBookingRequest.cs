namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>Back from the Payment step: the booking's own draft id.</summary>
public sealed record ReleaseBookingRequest(
    string DraftId);
