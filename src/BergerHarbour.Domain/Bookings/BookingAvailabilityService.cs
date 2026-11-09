using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Overlap rules over loaded bookings, unavailabilities and blocked periods (§3, decision 15).</summary>
public sealed class BookingAvailabilityService
{
    /// <summary>
    /// The blocking bookings (non-stand-by Active, unexpired non-stand-by holds) and unavailabilities that overlap the stay.
    /// </summary>
    public AvailabilityCheck Check(
        StayRange stay, IEnumerable<Booking> bookings, IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now,
        BookingId? excluding = null) =>
        new(
            bookings.Where(b => b.Id != excluding && b.BlocksAvailability(now) && b.Stay.Overlaps(stay)).ToList(),
            unavailabilities.Where(u => u.Nights.Overlaps(stay)).ToList());

    /// <summary>The availability invariant: throws 409 "Those dates are no longer available." naming the conflicts.</summary>
    public void EnsureAvailable(
        StayRange stay, IEnumerable<Booking> bookings, IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now,
        BookingId? excluding = null)
    {
        var check = Check(stay, bookings, unavailabilities, now, excluding);
        if (!check.IsAvailable)
        {
            throw new ConflictException(ConflictException.DatesTaken, check.References);
        }
    }

    public bool IsFree(StayRange stay, IEnumerable<Booking> bookings, IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now) =>
        Check(stay, bookings, unavailabilities, now).IsAvailable;

    /// <summary>
    /// The Active stand-bys of the same boat that <paramref name="booking"/> supersedes, if it is an Active non-stand-by.
    /// </summary>
    public IReadOnlyList<Booking> StandbysSupersededBy(Booking booking, IEnumerable<Booking> bookings) =>
        booking.Status != BookingStatus.Active || booking.IsStandby
            ? []
            : bookings.Where(b => b.Id != booking.Id && b.BoatId == booking.BoatId && b.IsStandby && b.Status == BookingStatus.Active
                && b.Stay.Overlaps(booking.Stay)).ToList();

    /// <summary>Blocked periods the stay overlaps (online stays may not overlap any).</summary>
    public IReadOnlyList<BlockedPeriod> OverlappingBlockedPeriods(StayRange stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.Where(p => p.Overlaps(stay)).ToList();

    /// <summary>An unavailability may not overlap an Active or (unexpired) PendingPayment non-stand-by booking.</summary>
    public void EnsureUnavailabilityAllowed(NightRange nights, IEnumerable<Booking> bookings, DateTimeOffset now)
    {
        var clashes = bookings.Where(b => b.BlocksAvailability(now) && b.Stay.Overlaps(nights)).ToList();
        if (clashes.Count > 0)
        {
            var references = clashes.Select(b => b.Reference?.Value ?? "a pending online booking").Distinct().ToList();
            throw new ConflictException($"Those nights overlap {string.Join(", ", references)}.", references);
        }
    }
}
