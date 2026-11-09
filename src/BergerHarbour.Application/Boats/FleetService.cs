using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Boats;

public sealed record BoatRateDto(string SeasonId, PeriodType PeriodType, decimal Price);

public sealed record AdminBoat(
    string Id,
    string Name,
    string Slug,
    BoatType Type,
    int Berth,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal? DepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly,
    bool IsActive,
    IReadOnlyList<BoatRateDto> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    IReadOnlyList<string> MissingForActivation,
    int Version);

public sealed record SaveBoatRequest(
    string Name,
    string Slug,
    int Berth,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal? DepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly,
    bool IsActive,
    IReadOnlyList<BoatRateDto>? Rates,
    IReadOnlyList<string>? AllowedAddonIds,
    int Version);

public sealed record UnavailabilityDto(string Id, string BoatId, DateOnly FirstNight, DateOnly LastNight, string? Comments, int Version);
public sealed record SaveUnavailabilityRequest(DateOnly FirstNight, DateOnly LastNight, string? Comments, int Version);

/// <summary>The Fleet page: boats and their unavailabilities.</summary>
public sealed class FleetService(
    IBoatRepository boats,
    IBoatUnavailabilityRepository unavailabilities,
    ISeasonRepository seasons,
    IAddonDefinitionRepository addons,
    IBoatScheduleLock scheduleLock,
    IClock clock)
{
    public async Task<IReadOnlyList<AdminBoat>> ListAsync(CancellationToken ct = default)
    {
        var s = await seasons.ListAsync(ct);
        return (await boats.ListAsync(ct)).InDisplayOrder().Select(b => ToDto(b, s)).ToList();
    }

    public async Task<AdminBoat> GetAsync(string id, CancellationToken ct = default) =>
        ToDto(await LoadAsync(id, ct), await seasons.ListAsync(ct));

    private async Task<Boat> LoadAsync(string id, CancellationToken ct) =>
        await boats.GetAsync(new BoatId(id), ct) ?? throw new NotFoundException("Boat not found.");

    private static AdminBoat ToDto(Boat b, IReadOnlyList<Season> seasons) => new(
        b.Id.Value, b.Name, b.Slug, b.Type, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription, b.SecurityBond, b.DepositAmount,
        b.ImageUrl, b.HasSpa, b.IsPetFriendly, b.IsActive, b.Rates.Select(r => new BoatRateDto(r.SeasonId, r.PeriodType, r.Price)).ToList(),
        b.AllowedAddonIds, b.MissingForActivation(seasons), b.Version);

    public Task<AdminBoat> CreateAsync(SaveBoatRequest r, CancellationToken ct = default) => SaveAsync(null, r, ct);
    public Task<AdminBoat> UpdateAsync(string id, SaveBoatRequest r, CancellationToken ct = default) => SaveAsync(id, r, ct);

    private async Task<AdminBoat> SaveAsync(string? id, SaveBoatRequest r, CancellationToken ct)
    {
        var details = new BoatDetails(r.Name, r.Slug, BoatType.House, r.Berth, r.AdultBeds, r.KidsBeds, r.BeddingDescription,
            r.SecurityBond, r.DepositAmount, r.ImageUrl, r.HasSpa, r.IsPetFriendly);
        Boat boat;
        if (id is null) boat = Boat.Create(details, clock.UtcNow);
        else
        {
            boat = await LoadAsync(id, ct);
            boat.EnsureVersion(r.Version);
            boat.UpdateDetails(details);
        }
        var slugOwner = await boats.GetBySlugAsync(boat.Slug, ct);
        if (slugOwner is not null && slugOwner.Id != boat.Id)
            throw new DomainValidationException("slug", $"{slugOwner.Name} already uses this slug.");

        var allSeasons = await seasons.ListAsync(ct);
        var knownSeasons = allSeasons.Select(s => s.Id).ToHashSet();
        boat.SetRates((r.Rates ?? []).Where(x => knownSeasons.Contains(x.SeasonId)).Select(x => new BoatRate(x.SeasonId, x.PeriodType, x.Price)));
        var knownAddons = (await addons.ListAsync(ct)).Select(a => a.Id).ToHashSet();
        boat.SetAllowedAddons((r.AllowedAddonIds ?? []).Where(knownAddons.Contains));
        if (r.IsActive) boat.Activate(allSeasons); // shows what's missing when the invariant fails
        else boat.Deactivate();
        await boats.SaveAsync(boat, ct);
        return ToDto(boat, allSeasons);
    }

    // ----- Unavailabilities -----

    public async Task<IReadOnlyList<UnavailabilityDto>> ListUnavailabilitiesAsync(string boatId, CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        return (await unavailabilities.ListForBoatAsync(boat.Id, ct)).OrderByDescending(u => u.Nights.FirstNight).Select(ToDto).ToList();
    }

    private static UnavailabilityDto ToDto(BoatUnavailability u) =>
        new(u.Id, u.BoatId.Value, u.Nights.FirstNight, u.Nights.LastNight, u.Comments, u.Version);

    public async Task<UnavailabilityDto> CreateUnavailabilityAsync(string boatId, SaveUnavailabilityRequest r, CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        var now = clock.UtcNow;
        var created = BoatUnavailability.Create(boat.Id, r.FirstNight, r.LastNight, r.Comments, now);
        await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var bookings = await session.GetPotentiallyOverlappingBookingsAsync(created.AsStay);
            BookingAvailabilityService.EnsureUnavailabilityAllowed(created.Nights, bookings, now);
            session.SaveUnavailability(created);
            return created;
        }, ct);
        return ToDto(created);
    }

    public async Task<UnavailabilityDto> UpdateUnavailabilityAsync(string boatId, string id, SaveUnavailabilityRequest r, CancellationToken ct = default)
    {
        var boat = await LoadAsync(boatId, ct);
        var now = clock.UtcNow;
        var saved = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var u = await session.GetUnavailabilityAsync(id);
            if (u is null || u.BoatId != boat.Id) throw new NotFoundException("Unavailability not found.");
            u.EnsureVersion(r.Version);
            u.Update(r.FirstNight, r.LastNight, r.Comments);
            var bookings = await session.GetPotentiallyOverlappingBookingsAsync(u.AsStay);
            BookingAvailabilityService.EnsureUnavailabilityAllowed(u.Nights, bookings, now);
            session.SaveUnavailability(u);
            return u;
        }, ct);
        return ToDto(saved);
    }

    public async Task DeleteUnavailabilityAsync(string boatId, string id, CancellationToken ct = default)
    {
        var u = await unavailabilities.GetAsync(id, ct);
        if (u is null || u.BoatId.Value != boatId) throw new NotFoundException("Unavailability not found.");
        await unavailabilities.DeleteAsync(u, ct);
    }
}
