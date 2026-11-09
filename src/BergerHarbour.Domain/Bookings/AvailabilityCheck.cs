using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Domain.Bookings;

/// <summary>The bookings and unavailabilities a stay conflicts with.</summary>
public sealed record AvailabilityCheck(IReadOnlyList<Booking> ConflictingBookings, IReadOnlyList<BoatUnavailability> ConflictingUnavailabilities)
{
    public bool IsAvailable => ConflictingBookings.Count == 0 && ConflictingUnavailabilities.Count == 0;

    /// <summary>The conflicting bookings' references ("Pending online booking" for holds, which have none yet).</summary>
    public IReadOnlyList<string> References =>
        ConflictingBookings.Select(b => b.Reference?.Value ?? "Pending online booking")
            .Concat(ConflictingUnavailabilities.Select(_ => "Boat unavailable"))
            .Distinct()
            .ToList();
}
