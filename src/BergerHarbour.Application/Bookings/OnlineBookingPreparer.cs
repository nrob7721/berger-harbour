using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// Loads the validation context for an online request, runs the channel's strategy, and prices the stay. Prices sent
/// by the booking app are never used.
/// </summary>
public sealed class OnlineBookingPreparer
{
    private readonly IBoatRepository boats;
    private readonly IBookingRepository bookings;
    private readonly ReferenceDataLoader referenceData;
    private readonly IBookingRequestValidationStrategy strategy;
    private readonly PricingService pricing;
    private readonly PaymentScheduleService schedule;
    private readonly AddonCapacityService capacity;
    private readonly StayPeriodFactory periods;
    private readonly IClock clock;

    public OnlineBookingPreparer(
        IBoatRepository boats, IBookingRepository bookings, ReferenceDataLoader referenceData, IBookingRequestValidationStrategy strategy,
        PricingService pricing, PaymentScheduleService schedule, AddonCapacityService capacity, StayPeriodFactory periods, IClock clock)
    {
        this.boats = boats;
        this.bookings = bookings;
        this.referenceData = referenceData;
        this.strategy = strategy;
        this.pricing = pricing;
        this.schedule = schedule;
        this.capacity = capacity;
        this.periods = periods;
        this.clock = clock;
    }

    /// <summary>The house boat for a slug, active or not; 404 for an unknown slug or a non-House boat.</summary>
    public async Task<Boat> HouseBoatAsync(string slug, CancellationToken ct)
    {
        var boat = await boats.GetBySlugAsync(slug, ct);
        return boat is { Type: BoatType.House } ? boat : throw new NotFoundException("We couldn't find that boat.");
    }

    public Task<Boat?> BoatByIdAsync(BoatId id, CancellationToken ct) => boats.GetAsync(id, ct);

    public async Task<PreparedOnlineBooking> PrepareAsync(
        Boat boat, BookingRequest request, BookingValidationScope scope, BookingDraft? draft, CancellationToken ct)
    {
        var reference = await referenceData.LoadAsync(ct);
        var now = clock.UtcNow;
        var remaining = await RemainingCapacityAsync(request, reference, now, ct);
        var context = new BookingValidationContext(boat, reference.Settings, reference.BlockedPeriods, reference.Addons, Sydney.DateOf(now), scope,
            draft, remaining);
        strategy.Validate(request, context).ThrowIfAny();

        var stay = periods.ForOnlineStart(request.PeriodType, request.StartDate);
        var season = pricing.SeasonFor(stay.Start, reference.Seasons);
        var hirePrice = pricing.HirePrice(boat, request.PeriodType, stay.Start, reference.Seasons)
            ?? throw new BusinessRuleException("We can't price these dates online. Please call or email us.");
        var lines = request.Addons
            .Select(a => reference.Addon(a.AddonId)!)
            .OrderBy(a => a.SortOrder)
            .Select(a => AddonLine.Create(null, a.Id, a.Name, request.Addons.First(r => r.AddonId == a.Id).Quantity,
                pricing.AddonUnitPrice(a, request.PeriodType)))
            .ToList();
        var deposit = boat.EffectiveDeposit(reference.Settings);
        return new PreparedOnlineBooking(boat, reference, stay, season, hirePrice, lines, deposit, PaymentScheduleService.CheckoutAmount(deposit, hirePrice),
            schedule.ScheduleFor(stay, reference.BlockedPeriods), boat.RoomingWarningFor(request.NumberOfAdults + request.NumberOfChildren));
    }

    /// <summary>Remaining capacity of each requested capacity-limited add-on, checked without a lock (§7.5).</summary>
    private async Task<IReadOnlyDictionary<AddonId, int>> RemainingCapacityAsync(
        BookingRequest request, ReferenceData reference, DateTimeOffset now, CancellationToken ct)
    {
        var result = new Dictionary<AddonId, int>();
        if (periods.TryOnlineStart(request.PeriodType, request.StartDate) is not { } stay)
        {
            return result;
        }

        foreach (var requested in request.Addons.DistinctBy(a => a.AddonId))
        {
            if (reference.Addon(requested.AddonId) is { HasCapacity: true } addon)
            {
                var used = await bookings.ListWithAddonAsync(addon.Id, stay.Start, stay.End, ct);
                result[addon.Id] = capacity.Remaining(addon, stay, used, now);
            }
        }

        return result;
    }
}
