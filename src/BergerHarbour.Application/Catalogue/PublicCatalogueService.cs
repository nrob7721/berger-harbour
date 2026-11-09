using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Catalogue;

/// <summary>Customer-safe fleet, availability and capacity data. Never returns booking or customer data.</summary>
public sealed class PublicCatalogueService(
    IBoatRepository boats,
    IBookingRepository bookings,
    IBoatUnavailabilityRepository unavailabilities,
    ReferenceDataLoader referenceData,
    IClock clock)
{
    public const int MaxAvailabilityMonths = 15;

    public async Task<PublicBoatsResponse> GetBoatsAsync(CancellationToken ct = default)
    {
        var all = await boats.ListAsync(ct);
        var settings = await referenceData.GetSettingsAsync(ct);
        var list = all.Where(b => b.IsBookableHouseBoat).InDisplayOrder()
            .Select(b => new PublicBoatSummary(b.Slug, b.Name, b.ImageUrl, b.Berth, b.HasSpa, b.IsPetFriendly))
            .ToList();
        return new PublicBoatsResponse(list, new PublicBookingSettings(
            settings.MinimumLeadTimeDays, settings.BookingsOpenUntil, SydneyTime.Today(clock.UtcNow),
            settings.ContactPhone, settings.ContactEmail, settings.HireTermsUrl));
    }

    public async Task<PublicBoatDetails> GetBoatAsync(string slug, CancellationToken ct = default)
    {
        var boat = await boats.GetBySlugAsync(slug, ct);
        if (boat is null || boat.Type != BoatType.House) throw new NotFoundException("We couldn't find that boat.");
        var reference = await referenceData.LoadAsync(ct);
        var addons = reference.Addons
            .Where(a => a.IsActive && boat.AllowedAddonIds.Contains(a.Id))
            .OrderBy(a => a.SortOrder)
            .Select(a => new PublicAddon(a.Id, a.Name, a.Description, a.PriceOnRequest, a.QuantityApplies,
                new PublicAddonPrices(a.PriceFor(PeriodType.Midweek), a.PriceFor(PeriodType.Weekend), a.PriceFor(PeriodType.Week)),
                a.HasCapacity, a.CapacityUnitLabel))
            .ToList();
        return new PublicBoatDetails(boat.Slug, boat.Name, boat.ImageUrl, boat.Berth, boat.HasSpa, boat.IsPetFriendly,
            boat.AdultBeds ?? 0, boat.KidsBeds ?? 0, boat.BeddingDescription, boat.SecurityBond,
            boat.EffectiveDeposit(reference.Settings.DepositAmount), boat.IsBookableHouseBoat, addons);
    }

    /// <summary>Ranges only: each active house boat's blocking bookings and unavailabilities, plus blocked periods.</summary>
    public async Task<FleetAvailabilityResponse> GetAvailabilityAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to <= from) throw new DomainValidationException("to", "'to' must be after 'from'.");
        if (to > from.AddMonths(MaxAvailabilityMonths)) throw new DomainValidationException("to", $"Ask for at most {MaxAvailabilityMonths} months at a time.");
        var now = clock.UtcNow;
        var fleet = (await boats.ListAsync(ct)).Where(b => b.IsBookableHouseBoat).InDisplayOrder().ToList();
        var blocking = (await bookings.ListOverlappingAsync(from, to, BookingStatuses.Blocking, ct)).Where(b => b.BlocksAvailability(now)).ToList();
        var unavailable = await unavailabilities.ListInRangeAsync(from, to, ct);
        var reference = await referenceData.LoadAsync(ct);
        var window = new StayRange(from, to);

        var enquire = reference.BlockedPeriods.Where(p => window.Overlaps(p.Nights)).OrderBy(p => p.Nights.FirstNight)
            .Select(p => new EnquireOnlyRange(p.Name, p.Nights.FirstNight, p.Nights.LastNight)).ToList();
        var result = fleet.Select(boat => new BoatAvailability(boat.Slug,
            blocking.Where(b => b.BoatId == boat.Id).Select(b => new StayRangeDto(b.StartDate, b.EndDate))
                .Concat(unavailable.Where(u => u.BoatId == boat.Id).Select(u => new StayRangeDto(u.AsStay.Start, u.AsStay.End)))
                .OrderBy(r => r.Start).ToList())).ToList();
        return new FleetAvailabilityResponse(enquire, result);
    }

    /// <summary>For one online stay: whether each active house boat is free, and its hire price.</summary>
    public async Task<PeriodAvailabilityResponse> GetPeriodAvailabilityAsync(PeriodType periodType, DateOnly startDate, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var reference = await referenceData.LoadAsync(ct);
        var errors = new ValidationErrors();
        if (!StayPeriodFactory.OnlinePeriodTypes.Contains(periodType))
            errors.Add("periodType", "Choose a mid-week, weekend or week stay.");
        var stay = errors.Any ? null : OnlineStayRules.Validate(periodType, startDate, reference, now, errors);
        errors.ThrowIfAny();
        var s = stay!.Value;

        var fleet = (await boats.ListAsync(ct)).Where(b => b.IsBookableHouseBoat).InDisplayOrder().ToList();
        var blocking = await bookings.ListOverlappingAsync(s.Start, s.End, BookingStatuses.Blocking, ct);
        var unavailable = await unavailabilities.ListInRangeAsync(s.Start, s.End, ct);
        var result = fleet.Select(boat => new PeriodBoatAvailability(
            boat.Slug,
            !BookingAvailabilityService.FindConflicts(s, blocking.Where(b => b.BoatId == boat.Id), unavailable.Where(u => u.BoatId == boat.Id), now).Any,
            PricingService.HirePrice(boat, reference.Seasons, periodType, s.Start))).ToList();
        return new PeriodAvailabilityResponse(periodType, s.Start, s.End, result);
    }

    /// <summary>Remaining capacity of a capacity-limited add-on across all boats; 404 for an add-on without a capacity.</summary>
    public async Task<AddonRemainingResponse> GetAddonRemainingAsync(string addonId, DateOnly startDate, DateOnly endDate,
        string? excludeBookingId = null, CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        var addon = reference.Addon(addonId);
        if (addon?.Capacity is not { } capacity) throw new NotFoundException("That add-on has no capacity limit.");
        var stay = new StayRange(startDate, endDate);
        var related = await bookings.ListWithAddonAsync(addonId, stay.Start, stay.End, ct);
        return new AddonRemainingResponse(AddonCapacityService.Remaining(capacity, addonId, stay, related, clock.UtcNow, excludeBookingId));
    }
}

