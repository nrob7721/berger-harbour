using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Cross-aggregate overlap rules. Callers load the boat's potentially overlapping bookings and unavailabilities
/// (inside the per-boat exclusive section for writes) and pass them in.
/// </summary>
public static class BookingAvailabilityService
{
    /// <summary>Bookings of the boat that block availability and overlap the stay.</summary>
    public static IReadOnlyList<Booking> ConflictingBookings(BoatId boatId, Stay stay, IEnumerable<Booking> bookings,
        DateTimeOffset now, BookingId? excluding = null) =>
        bookings.Where(b => b.BoatId == boatId && b.Id != excluding && b.BlocksAvailability(now) && b.Stay.Overlaps(stay))
            .ToList();

    public static IReadOnlyList<BoatUnavailability> ConflictingUnavailabilities(BoatId boatId, Stay stay,
        IEnumerable<BoatUnavailability> unavailabilities) =>
        unavailabilities.Where(u => u.BoatId == boatId && stay.Overlaps(u.Nights)).ToList();

    /// <summary>
    /// The availability invariant (decision 15): a booking may never overlap an Active or unexpired PendingPayment
    /// non-stand-by booking, or an unavailability. This applies to staff and customers, and to stand-by bookings,
    /// which ignore other stand-bys.
    /// </summary>
    public static void EnsureAvailable(BoatId boatId, Stay stay, IEnumerable<Booking> bookings,
        IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now, BookingId? excluding = null)
    {
        var conflicts = ConflictingBookings(boatId, stay, bookings, now, excluding).Select(b => b.Reference.Value)
            .Concat(ConflictingUnavailabilities(boatId, stay, unavailabilities)
                .Select(u => $"Unavailable {u.Nights.FirstNight:dd/MM/yyyy}–{u.Nights.LastNight:dd/MM/yyyy}"))
            .ToList();
        if (conflicts.Count > 0)
        {
            throw new AvailabilityConflictException(AvailabilityConflictException.DatesNoLongerAvailable, conflicts);
        }
    }

    /// <summary>
    /// An unavailability may not overlap an Active or PendingPayment non-stand-by booking. The error names the
    /// booking reference(s).
    /// </summary>
    public static void EnsureUnavailabilityAllowed(BoatId boatId, NightRange nights, IEnumerable<Booking> bookings,
        DateTimeOffset now)
    {
        var conflicts = ConflictingBookings(boatId, nights.ToStay(), bookings, now).Select(b => b.Reference.Value).ToList();
        if (conflicts.Count > 0)
        {
            throw new AvailabilityConflictException(
                $"The boat is booked on those nights: {string.Join(", ", conflicts)}.", conflicts);
        }
    }

    public static IReadOnlyList<BlockedPeriod> OverlappingBlockedPeriods(Stay stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.Where(p => stay.Overlaps(p.Nights)).ToList();

    /// <summary>Active stand-by bookings that the given Active non-stand-by booking supersedes.</summary>
    public static IReadOnlyList<Booking> StandbysSupersededBy(Booking booking, IEnumerable<Booking> candidates)
    {
        if (booking.IsStandby || booking.Status != BookingStatus.Active)
        {
            return [];
        }

        return candidates.Where(c => c.IsStandby && c.Status == BookingStatus.Active && c.BoatId == booking.BoatId &&
                                     c.Id != booking.Id && c.Stay.Overlaps(booking.Stay))
            .ToList();
    }
}
