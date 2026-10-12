using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// Non-blocking warnings for staff bookings: guests above the boat's maximum or beds, add-on capacity exceeded,
/// and a custom check-in/check-out time that clashes with a same-day changeover on the boat.
/// </summary>
public static class StaffBookingWarnings
{
    /// <param name="boatBookings">The boat's bookings around the stay (for changeover clashes).</param>
    /// <param name="capacityBookings">Bookings of all boats with the booking's capacity-limited add-ons.</param>
    public static IReadOnlyList<string> For(Boat boat, Stay stay, int adults, int children, TimeOnly checkInTime,
        TimeOnly checkOutTime, IEnumerable<(AddonDefinition Addon, int Quantity)> addons, IEnumerable<Booking> boatBookings,
        IEnumerable<Booking> capacityBookings, DateTimeOffset now, BookingId? self)
    {
        var warnings = new List<string>();
        var guests = adults + children;
        if (guests > boat.MaxNoOfGuests)
        {
            warnings.Add($"{guests} guests is more than {boat.Name}'s maximum of {boat.MaxNoOfGuests}.");
        }
        else if (boat.RequiresRoomingWarning(guests))
        {
            warnings.Add($"{guests} guests but only {boat.TotalBeds} beds ({boat.AdultBeds} adult, {boat.KidsBeds ?? 0} kids); " +
                         "some guests will share.");
        }

        var capacityList = capacityBookings.ToList();
        foreach (var (addon, quantity) in addons.Where(a => a.Addon.HasCapacity))
        {
            if (AddonCapacityService.FirstNightExceeded(addon, stay, quantity, capacityList, now, self) is { } night)
            {
                warnings.Add($"This exceeds {addon.Name.ToLowerInvariant()} capacity on {Format.Date(night)}.");
            }
        }

        foreach (var other in boatBookings.Where(b => b.Id != self && b.BoatId == boat.Id && b.BlocksAvailability(now)))
        {
            if (other.EndDate == stay.StartDate && other.CheckOutTime > checkInTime)
            {
                warnings.Add($"Check-in at {Format.Time(checkInTime)} on {Format.Date(stay.StartDate)} is before " +
                             $"{other.Reference.Value} checks out at {Format.Time(other.CheckOutTime)}.");
            }

            if (other.StartDate == stay.EndDate && other.CheckInTime < checkOutTime)
            {
                warnings.Add($"Check-out at {Format.Time(checkOutTime)} on {Format.Date(stay.EndDate)} is after " +
                             $"{other.Reference.Value} checks in at {Format.Time(other.CheckInTime)}.");
            }
        }

        return warnings;
    }
}
