using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// Capacity shared across all boats and bookings, counted per night (§7.5). Only bookings that block availability count.
/// </summary>
public sealed class AddonCapacityService
{
    /// <summary>Σ quantity of the add-on over blocking bookings whose stay includes the night.</summary>
    public int UsedOnNight(AddonId addonId, DateOnly night, IEnumerable<Booking> bookings, DateTimeOffset now, BookingId? excluding = null) =>
        bookings
            .Where(b => b.Id != excluding && b.BlocksAvailability(now) && b.Stay.IncludesNight(night))
            .SelectMany(b => b.AddonLines)
            .Where(l => l.AddonId == addonId)
            .Sum(l => l.Quantity);

    /// <summary>max(0, Capacity − the busiest night's use). Unlimited (int.MaxValue) without a capacity.</summary>
    public int Remaining(AddonDefinition addon, StayRange stay, IEnumerable<Booking> bookings, DateTimeOffset now, BookingId? excluding = null)
    {
        if (addon.Capacity is not { } capacity)
        {
            return int.MaxValue;
        }

        var list = bookings as IReadOnlyCollection<Booking> ?? bookings.ToList();
        var busiest = stay.EachNight().Max(night => UsedOnNight(addon.Id, night, list, now, excluding));
        return Math.Max(0, capacity - busiest);
    }

    /// <summary>The nights on which adding <paramref name="quantity"/> exceeds the capacity (staff get a warning).</summary>
    public IReadOnlyList<DateOnly> NightsOverCapacity(
        AddonDefinition addon, StayRange stay, int quantity, IEnumerable<Booking> bookings, DateTimeOffset now, BookingId? excluding = null)
    {
        if (addon.Capacity is not { } capacity)
        {
            return [];
        }

        var list = bookings as IReadOnlyCollection<Booking> ?? bookings.ToList();
        return stay.EachNight().Where(night => UsedOnNight(addon.Id, night, list, now, excluding) + quantity > capacity).ToList();
    }
}
