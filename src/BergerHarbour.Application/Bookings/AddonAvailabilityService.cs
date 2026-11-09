using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>Remaining capacity of a capacity-limited add-on (<c>AddonAvailabilityController</c> in both APIs).</summary>
public sealed class AddonAvailabilityService
{
    private readonly IAddonDefinitionRepository addons;
    private readonly IBookingRepository bookings;
    private readonly AddonCapacityService capacity;
    private readonly IClock clock;

    public AddonAvailabilityService(IAddonDefinitionRepository addons, IBookingRepository bookings, AddonCapacityService capacity, IClock clock)
    {
        this.addons = addons;
        this.bookings = bookings;
        this.capacity = capacity;
        this.clock = clock;
    }

    /// <summary>
    /// <c>{ remaining }</c> across all boats for <c>[startDate, endDate)</c>. 404 if the add-on has no capacity. Only staff
    /// may exclude the booking being edited.
    /// </summary>
    public async Task<AddonRemainingResponse> GetRemainingAsync(
        string addonId, DateOnly? startDate, DateOnly? endDate, string? excludeBookingId, CancellationToken ct = default)
    {
        var addon = await addons.GetAsync(new AddonId(addonId), ct);
        if (addon is not { HasCapacity: true })
        {
            throw new NotFoundException("That add-on has no capacity limit.");
        }

        if (startDate is not { } start || endDate is not { } end)
        {
            throw new DomainValidationException(startDate is null ? "startDate" : "endDate", "Enter both dates.");
        }

        var stay = StayRange.Create(start, end);
        var used = await bookings.ListWithAddonAsync(addon.Id, start, end, ct);
        var exclude = string.IsNullOrWhiteSpace(excludeBookingId) ? (BookingId?)null : new BookingId(excludeBookingId);
        return new AddonRemainingResponse(capacity.Remaining(addon, stay, used, clock.UtcNow, exclude));
    }
}
