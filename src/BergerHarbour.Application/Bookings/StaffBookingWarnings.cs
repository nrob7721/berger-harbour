using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// Staff's non-blocking warnings (§7.1): guests above berth, guests above beds, add-on capacity exceeded, and a custom
/// time clashing with a same-day changeover. Pure: it works on data the application service has loaded.
/// </summary>
public sealed class StaffBookingWarnings
{
    private readonly AddonCapacityService capacity;

    public StaffBookingWarnings(AddonCapacityService capacity)
    {
        this.capacity = capacity;
    }

    /// <param name="boat">The booking's boat.</param>
    /// <param name="booking">The booking as it will be saved.</param>
    /// <param name="addons">Every add-on definition.</param>
    /// <param name="boatNeighbours">The boat's blocking bookings that end on the start date or start on the end date.</param>
    /// <param name="capacityBookings">Bookings of all boats that use a capacity-limited add-on during the stay.</param>
    /// <param name="now">Now, to tell live holds from expired ones.</param>
    public IReadOnlyList<string> For(
        Boat boat, Booking booking, IReadOnlyList<AddonDefinition> addons, IEnumerable<Booking> boatNeighbours, IReadOnlyList<Booking> capacityBookings,
        DateTimeOffset now)
    {
        var warnings = new List<string>();
        var guests = booking.NumberOfGuests;
        if (guests > boat.Berth)
        {
            warnings.Add($"{guests} guests is more than {boat.Name}'s berth of {boat.Berth}.");
        }

        if (guests > boat.TotalBeds)
        {
            warnings.Add($"{guests} guests is more than {boat.Name}'s {boat.TotalBeds} beds ({boat.AdultBeds ?? 0} adult + {boat.KidsBeds ?? 0} kids). " +
                "Some guests will need to share beds.");
        }

        if (booking.BlocksAvailability(now))
        {
            foreach (var line in booking.AddonLines)
            {
                var addon = addons.FirstOrDefault(a => a.Id == line.AddonId);
                if (addon is not { HasCapacity: true })
                {
                    continue;
                }

                var nights = capacity.NightsOverCapacity(addon, booking.Stay, line.Quantity, capacityBookings, now, booking.Id);
                if (nights.Count > 0)
                {
                    warnings.Add($"This exceeds {addon.Name.ToLowerInvariant()} capacity on {DisplayFormat.Date(nights[0])}.");
                }
            }
        }

        foreach (var other in boatNeighbours.Where(b => b.Id != booking.Id && b.BoatId == booking.BoatId && b.BlocksAvailability(now)))
        {
            var name = other.Reference?.Value ?? "a pending online booking";
            if (other.EndDate == booking.StartDate && booking.CheckInTime < other.CheckOutTime)
            {
                warnings.Add($"Check-in at {DisplayFormat.Time(booking.CheckInTime)} on {DisplayFormat.Date(booking.StartDate)} is before " +
                    $"{name}'s check-out at {DisplayFormat.Time(other.CheckOutTime)}.");
            }

            if (other.StartDate == booking.EndDate && booking.CheckOutTime > other.CheckInTime)
            {
                warnings.Add($"Check-out at {DisplayFormat.Time(booking.CheckOutTime)} on {DisplayFormat.Date(booking.EndDate)} is after " +
                    $"{name}'s check-in at {DisplayFormat.Time(other.CheckInTime)}.");
            }
        }

        return warnings;
    }
}
