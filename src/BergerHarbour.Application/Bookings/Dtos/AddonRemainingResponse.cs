namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>Remaining capacity of a capacity-limited add-on for a stay, across all boats.</summary>
public sealed record AddonRemainingResponse(
    int Remaining);
