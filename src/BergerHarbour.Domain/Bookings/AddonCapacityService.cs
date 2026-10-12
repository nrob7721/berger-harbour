using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Capacity-limited add-ons (e.g. 11 car spots) are shared across all boats and bookings and counted per night.
/// Only bookings that block availability count. Custom check-in/check-out times do not affect the count.
/// </summary>
public static class AddonCapacityService
{
    /// <summary>Units of the add-on in use on a night, over bookings of all boats.</summary>
    public static int UsedOnNight(AddonId addonId, DateOnly night, IEnumerable<Booking> bookings, DateTimeOffset now,
        BookingId? excluding = null) =>
        Counted(bookings, now, excluding).Where(b => b.Stay.ContainsNight(night)).Sum(b => b.QuantityOf(addonId));

    /// <summary>What is left for a stay, measured on its busiest night.</summary>
    public static int Remaining(AddonDefinition addon, Stay stay, IEnumerable<Booking> bookings, DateTimeOffset now,
        BookingId? excluding = null)
    {
        var capacity = addon.Capacity ?? throw new InvalidOperationException($"{addon.Name} has no capacity.");
        var counted = Counted(bookings, now, excluding).ToList();
        var busiest = stay.EachNight().Select(n => UsedOnNight(addon.Id, n, counted, now)).DefaultIfEmpty(0).Max();
        return Math.Max(0, capacity - busiest);
    }

    /// <summary>
    /// The first night on which adding <paramref name="quantity"/> units would exceed the capacity, or null when it
    /// fits. Staff only get a warning; online it is a hard limit.
    /// </summary>
    public static DateOnly? FirstNightExceeded(AddonDefinition addon, Stay stay, int quantity, IEnumerable<Booking> bookings,
        DateTimeOffset now, BookingId? excluding = null)
    {
        if (addon.Capacity is not { } capacity || quantity <= 0)
        {
            return null;
        }

        var counted = Counted(bookings, now, excluding).ToList();
        foreach (var night in stay.EachNight())
        {
            if (UsedOnNight(addon.Id, night, counted, now) + quantity > capacity)
            {
                return night;
            }
        }

        return null;
    }

    private static IEnumerable<Booking> Counted(IEnumerable<Booking> bookings, DateTimeOffset now, BookingId? excluding) =>
        bookings.Where(b => b.Id != excluding && b.BlocksAvailability(now));
}
