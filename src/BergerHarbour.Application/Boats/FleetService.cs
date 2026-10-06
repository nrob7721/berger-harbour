using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Boats;

/// <summary>Staff use cases for the Fleet page: boats and their unavailabilities.</summary>
public sealed class FleetService(
    IBoatRepository boats,
    ISeasonRepository seasons,
    IAddonDefinitionRepository addons,
    IBoatUnavailabilityRepository unavailabilities,
    IBoatScheduleLock scheduleLock,
    IClock clock)
{
    public async Task<IReadOnlyList<BoatSummaryDto>> ListAsync(CancellationToken ct = default) =>
        (await boats.ListAsync(ct)).OrderBy(b => b.Name)
        .Select(b => new BoatSummaryDto(b.Id.Value, b.Name, b.Slug, b.Type, b.MaxNoOfGuests, b.NoOfBeds, b.IsActive))
        .ToList();

    public async Task<BoatDetailDto> GetAsync(string id, CancellationToken ct = default) =>
        ToDto(await LoadAsync(id, ct), await seasons.ListAsync(ct));

    public async Task<BoatDetailDto> CreateAsync(BoatUpsertRequest request, CancellationToken ct = default)
    {
        await EnsureSlugIsFreeAsync(request.Slug, null, ct);
        var boat = Boat.Create(request.Name, request.Slug, BoatType.House, request.MaxNoOfGuests, clock.UtcNow);
        var allSeasons = await ApplyAsync(boat, request, ct);
        await boats.SaveAsync(boat, ct);
        return ToDto(boat, allSeasons);
    }

    public async Task<BoatDetailDto> UpdateAsync(string id, BoatUpsertRequest request, CancellationToken ct = default)
    {
        var boat = await LoadAsync(id, ct);
        boat.EnsureVersion(request.Version);
        await EnsureSlugIsFreeAsync(request.Slug, boat.Id, ct);
        var allSeasons = await ApplyAsync(boat, request, ct);
        await boats.SaveAsync(boat, ct);
        return ToDto(boat, allSeasons);
    }

    public async Task<IReadOnlyList<UnavailabilityDto>> ListUnavailabilitiesAsync(string boatId, CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        return (await unavailabilities.ListByBoatAsync(boat.Id, ct)).OrderByDescending(u => u.Nights.FirstNight).Select(ToDto).ToList();
    }

    public async Task<UnavailabilityDto> CreateUnavailabilityAsync(string boatId, UnavailabilityRequest request,
        CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        var nights = new NightRange(request.FirstNight, request.LastNight);
        var created = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var now = clock.UtcNow;
            var occupying = await session.GetOccupyingBookingsAsync(nights.ToStay());
            BookingAvailabilityService.EnsureUnavailabilityAllowed(boat.Id, nights, occupying, now);
            var unavailability = BoatUnavailability.Create(boat.Id, request.FirstNight, request.LastNight, request.Comments, now);
            session.Save(unavailability);
            return unavailability;
        }, ct);
        return ToDto(created);
    }

    public async Task<UnavailabilityDto> UpdateUnavailabilityAsync(string boatId, string id, UnavailabilityRequest request,
        CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        var nights = new NightRange(request.FirstNight, request.LastNight);
        var updated = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var now = clock.UtcNow;
            var unavailability = await session.GetUnavailabilityAsync(new UnavailabilityId(id));
            if (unavailability is null || unavailability.BoatId != boat.Id)
            {
                throw new NotFoundException("Unavailability not found.");
            }

            unavailability.EnsureVersion(request.Version);
            var occupying = await session.GetOccupyingBookingsAsync(nights.ToStay());
            BookingAvailabilityService.EnsureUnavailabilityAllowed(boat.Id, nights, occupying, now);
            unavailability.Update(request.FirstNight, request.LastNight, request.Comments, now);
            session.Save(unavailability);
            return unavailability;
        }, ct);
        return ToDto(updated);
    }

    /// <summary>Deleting only frees dates, so it does not need the per-boat exclusive section.</summary>
    public async Task DeleteUnavailabilityAsync(string boatId, string id, int version, CancellationToken ct = default)
    {
        var unavailability = await unavailabilities.GetAsync(new UnavailabilityId(id), ct);
        if (unavailability is null || unavailability.BoatId.Value != boatId)
        {
            throw new NotFoundException("Unavailability not found.");
        }

        unavailability.EnsureVersion(version);
        await unavailabilities.DeleteAsync(unavailability, ct);
    }

    private async Task<IReadOnlyList<Season>> ApplyAsync(Boat boat, BoatUpsertRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var allSeasons = await seasons.ListAsync(ct);
        var allAddons = await addons.ListAsync(ct);
        var errors = new ValidationErrors();
        foreach (var rate in request.Rates ?? [])
        {
            if (allSeasons.All(s => s.Id.Value != rate.SeasonId))
            {
                errors.Add("rates", $"Season {rate.SeasonId} does not exist.");
            }
        }

        foreach (var addonId in request.AllowedAddonIds ?? [])
        {
            if (allAddons.All(a => a.Id.Value != addonId))
            {
                errors.Add("allowedAddonIds", $"Add-on {addonId} does not exist.");
            }
        }

        errors.ThrowIfInvalid();
        boat.UpdateDetails(request.Name, request.Slug, request.MaxNoOfGuests, request.NoOfBeds, request.BeddingDescription,
            request.SecurityBond, now);
        boat.SetRates((request.Rates ?? []).Select(r => new BoatRate(new SeasonId(r.SeasonId), r.PeriodType, r.Price)), now);
        boat.SetAllowedAddons((request.AllowedAddonIds ?? []).Select(a => new AddonId(a)), now);
        if (request.IsActive && !boat.IsActive)
        {
            boat.Activate(allSeasons, now);
        }
        else if (!request.IsActive && boat.IsActive)
        {
            boat.Deactivate(now);
        }

        boat.EnsureStillComplete(allSeasons);
        return allSeasons;
    }

    private async Task EnsureSlugIsFreeAsync(string slug, BoatId? self, CancellationToken ct)
    {
        var existing = await boats.GetBySlugAsync((slug ?? string.Empty).Trim(), ct);
        if (existing is not null && existing.Id != self)
        {
            throw RequestValidationException.For("slug", $"The slug '{slug}' is already used by {existing.Name}.");
        }
    }

    private async Task<Boat> LoadAsync(string id, CancellationToken ct) =>
        await boats.GetAsync(new BoatId(id ?? string.Empty), ct) ?? throw new NotFoundException("Boat not found.");

    private static BoatDetailDto ToDto(Boat b, IReadOnlyList<Season> allSeasons) => new(
        b.Id.Value, b.Version, b.Name, b.Slug, b.Type, b.MaxNoOfGuests, b.NoOfBeds, b.BeddingDescription, b.SecurityBond,
        b.Rates.Select(r => new BoatRateDto(r.SeasonId.Value, r.PeriodType, r.Price)).ToList(),
        b.AllowedAddonIds.Select(a => a.Value).ToList(), b.IsActive, b.MissingActivationData(allSeasons), b.CreatedDate,
        b.ModifiedDate);

    private static UnavailabilityDto ToDto(BoatUnavailability u) => new(u.Id.Value, u.Version, u.BoatId.Value,
        u.Nights.FirstNight, u.Nights.LastNight, u.Comments, u.CreatedDate, u.ModifiedDate);
}
