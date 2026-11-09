using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Boats.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Boats;

/// <summary>The Fleet page (admin <c>BoatsController</c>). Boats are never deleted.</summary>
public sealed class BoatService
{
    private readonly IBoatRepository boats;
    private readonly ISeasonRepository seasons;
    private readonly IAddonDefinitionRepository addons;
    private readonly IClock clock;

    public BoatService(IBoatRepository boats, ISeasonRepository seasons, IAddonDefinitionRepository addons, IClock clock)
    {
        this.boats = boats;
        this.seasons = seasons;
        this.addons = addons;
        this.clock = clock;
    }

    /// <summary>Every boat, berth desc then name.</summary>
    public async Task<IReadOnlyList<AdminBoat>> ListAsync(CancellationToken ct = default)
    {
        var allSeasons = await seasons.ListAsync(ct);
        return Boat.InListOrder(await boats.ListAsync(ct)).Select(b => ToDto(b, allSeasons)).ToList();
    }

    public async Task<AdminBoat> GetAsync(string id, CancellationToken ct = default) =>
        ToDto(await LoadAsync(id, ct), await seasons.ListAsync(ct));

    public async Task<AdminBoat> CreateAsync(SaveBoatRequest request, CancellationToken ct = default)
    {
        var allSeasons = await seasons.ListAsync(ct);
        var details = await DetailsAsync(request, BoatType.House, null, allSeasons, ct);
        var boat = Boat.Create(BoatId.New(), details, clock.UtcNow);
        if (request.IsActive)
        {
            boat.Activate(allSeasons, clock.UtcNow);
        }

        await boats.SaveAsync(boat, ct);
        return ToDto(boat, allSeasons);
    }

    /// <summary>Edits every field, including the active flag (activation reports what is missing).</summary>
    public async Task<AdminBoat> UpdateAsync(string id, SaveBoatRequest request, CancellationToken ct = default)
    {
        var boat = await LoadAsync(id, ct);
        boat.EnsureVersion(request.Version);
        var allSeasons = await seasons.ListAsync(ct);
        var details = await DetailsAsync(request, boat.Type, boat.Id, allSeasons, ct);
        var now = clock.UtcNow;
        if (!request.IsActive)
        {
            boat.Deactivate(now);
        }

        boat.Update(details, allSeasons, now);
        if (request.IsActive)
        {
            boat.Activate(allSeasons, now);
        }

        await boats.SaveAsync(boat, ct);
        return ToDto(boat, allSeasons);
    }

    private static AdminBoat ToDto(Boat b, IReadOnlyList<Season> seasons) =>
        new(b.Id.Value, b.Name, b.Slug, b.Type, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription, b.SecurityBond, b.DepositAmount, b.ImageUrl,
            b.HasSpa, b.IsPetFriendly, b.IsActive, b.Rates.Select(r => new BoatRateDto(r.SeasonId.Value, r.PeriodType, r.Price)).ToList(),
            b.AllowedAddonIds.Select(a => a.Value).ToList(), b.MissingForActivation(seasons), b.Version);

    private async Task<Boat> LoadAsync(string id, CancellationToken ct) =>
        await boats.GetAsync(new BoatId(id), ct) ?? throw new NotFoundException("We couldn't find that boat.");

    private async Task<BoatDetails> DetailsAsync(SaveBoatRequest r, BoatType type, BoatId? self, IReadOnlyList<Season> allSeasons, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var slug = (r.Slug ?? string.Empty).Trim();
        if ((await boats.ListAsync(ct)).Any(b => b.Id != self && string.Equals(b.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("slug", "Another boat already uses this slug.");
        }

        var seasonIds = allSeasons.Select(s => s.Id.Value).ToHashSet();
        var rates = (r.Rates ?? []).ToList();
        if (rates.Any(rate => !seasonIds.Contains(rate.SeasonId)))
        {
            errors.Add("rates", "A rate refers to an unknown season.");
        }

        var addonIds = (await addons.ListAsync(ct)).Select(a => a.Id.Value).ToHashSet();
        var allowed = (r.AllowedAddonIds ?? []).Distinct().ToList();
        if (allowed.Any(a => !addonIds.Contains(a)))
        {
            errors.Add("allowedAddonIds", "An allowed add-on doesn't exist.");
        }

        errors.ThrowIfAny();
        return new BoatDetails(r.Name, slug, type, r.Berth, r.AdultBeds, r.KidsBeds, r.BeddingDescription, r.SecurityBond, r.DepositAmount, r.ImageUrl,
            r.HasSpa, r.IsPetFriendly, rates.Select(x => new BoatRate(new SeasonId(x.SeasonId), x.PeriodType, x.Price)).ToList(),
            allowed.Select(a => new AddonId(a)).ToList());
    }
}
