using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Boats.Dtos;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Boats;

/// <summary>The booking app's boat data (public <c>BoatsController</c>).</summary>
public sealed class BoatCatalogueService
{
    private readonly IBoatRepository boats;
    private readonly ReferenceDataLoader referenceData;
    private readonly IClock clock;

    public BoatCatalogueService(IBoatRepository boats, ReferenceDataLoader referenceData, IClock clock)
    {
        this.boats = boats;
        this.referenceData = referenceData;
        this.clock = clock;
    }

    /// <summary>Every active house boat (berth desc, then name) and the booking settings. Also the warm-up call.</summary>
    public async Task<PublicBoatsResponse> ListAsync(CancellationToken ct = default)
    {
        var settings = await referenceData.SettingsAsync(ct);
        var active = Boat.InListOrder((await boats.ListAsync(ct)).Where(b => b.IsActive && b.Type == BoatType.House))
            .Select(b => new PublicBoatSummary(b.Slug, b.Name, b.ImageUrl, b.Berth, b.HasSpa, b.IsPetFriendly))
            .ToList();
        return new PublicBoatsResponse(active, new PublicBookingSettings(settings.MinimumLeadTimeDays, settings.BookingsOpenUntil,
            Sydney.DateOf(clock.UtcNow), settings.ContactPhone, settings.ContactEmail?.Value, settings.HireTermsUrl));
    }

    /// <summary>Customer-safe data for one house boat, active or not; 404 for an unknown slug or a non-House boat.</summary>
    public async Task<PublicBoatDetails> GetAsync(string slug, CancellationToken ct = default)
    {
        var boat = await boats.GetBySlugAsync(slug, ct);
        if (boat is not { Type: BoatType.House })
        {
            throw new NotFoundException("We couldn't find that boat.");
        }

        var reference = await referenceData.LoadAsync(ct);
        var addons = reference.Addons
            .Where(a => a.IsActive && boat.AllowsAddon(a.Id))
            .OrderBy(a => a.SortOrder)
            .Select(ToPublic)
            .ToList();
        return new PublicBoatDetails(boat.Slug, boat.Name, boat.ImageUrl, boat.Berth, boat.HasSpa, boat.IsPetFriendly, boat.AdultBeds ?? 0,
            boat.KidsBeds ?? 0, boat.BeddingDescription, boat.SecurityBond, boat.EffectiveDeposit(reference.Settings), boat.IsActive, addons);
    }

    private static PublicAddon ToPublic(AddonDefinition a) =>
        new(a.Id.Value, a.Name, a.Description, a.PriceOnRequest, a.QuantityApplies,
            new PublicAddonPrices(a.PriceFor(PeriodType.Midweek), a.PriceFor(PeriodType.Weekend), a.PriceFor(PeriodType.Week)), a.HasCapacity,
            a.CapacityUnitLabel);
}
