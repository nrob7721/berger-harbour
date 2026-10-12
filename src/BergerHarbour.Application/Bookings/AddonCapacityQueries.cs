using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>Remaining capacity of capacity-limited add-ons (e.g. car parking), shared across all boats.</summary>
public sealed class AddonCapacityQueries(IAddonDefinitionRepository addons, IBookingRepository bookings, IClock clock)
{
    /// <summary>
    /// Abitrary roughly 2 month period picked to prevent abuse e.g. a request over 10 years would loop through too many bookings
    /// </summary>
    public const int MaxNights = 62;

    /// <summary>What is left for [start, end), optionally excluding a booking being edited. 404 without capacity.</summary>
    public async Task<AddonAvailabilityDto> RemainingAsync(string addonId, DateOnly startDate, DateOnly endDate,
        string? excludeBookingId = null, CancellationToken ct = default)
    {
        if (endDate <= startDate)
        {
            throw RequestValidationException.For("endDate", "The end date must be after the start date.");
        }

        if (endDate.DayNumber - startDate.DayNumber > MaxNights)
        {
            throw RequestValidationException.For("endDate", $"Choose a stay of up to {MaxNights} nights.");
        }

        var addon = await addons.GetAsync(new AddonId(addonId ?? string.Empty), ct);
        if (addon?.Capacity is not { } capacity)
        {
            throw new NotFoundException("That add-on has no capacity limit.");
        }

        var stay = new Stay(startDate, endDate);
        var remaining = AddonCapacityService.Remaining(addon, stay, await BookingsWithAsync(addon.Id, stay, ct), clock.UtcNow,
            string.IsNullOrEmpty(excludeBookingId) ? null : new BookingId(excludeBookingId));
        return new AddonAvailabilityDto(addon.Id.Value, capacity, remaining);
    }

    /// <summary>What is left of each capacity-limited add-on in the list, for the stay.</summary>
    public async Task<IReadOnlyDictionary<AddonId, int>> RemainingForAsync(IEnumerable<AddonDefinition> candidates, Stay stay,
        BookingId? excluding = null, CancellationToken ct = default)
    {
        var result = new Dictionary<AddonId, int>();
        var now = clock.UtcNow;
        foreach (var addon in candidates.Where(a => a.HasCapacity).DistinctBy(a => a.Id))
        {
            result[addon.Id] = AddonCapacityService.Remaining(addon, stay, await BookingsWithAsync(addon.Id, stay, ct), now, excluding);
        }

        return result;
    }

    /// <summary>Bookings of all boats with the add-on whose stay overlaps the given one.</summary>
    public Task<IReadOnlyList<Booking>> BookingsWithAsync(AddonId addonId, Stay stay, CancellationToken ct = default) =>
        bookings.ListWithAddonOverlappingAsync(addonId, stay.StartDate, stay.EndDate, ct);
}
