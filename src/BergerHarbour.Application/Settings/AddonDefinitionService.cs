using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>The add-on catalogue (<c>AddonDefinitionsController</c>). Delete deactivates; order changes only through reordering.</summary>
public sealed class AddonDefinitionService
{
    private readonly IAddonDefinitionRepository addons;

    public AddonDefinitionService(IAddonDefinitionRepository addons)
    {
        this.addons = addons;
    }

    public async Task<IReadOnlyList<AddonDto>> ListAsync(CancellationToken ct = default) =>
        (await addons.ListAsync(ct)).OrderBy(a => a.SortOrder).Select(ToDto).ToList();

    /// <summary>New add-ons go to the end.</summary>
    public async Task<AddonDto> CreateAsync(SaveAddonRequest r, CancellationToken ct = default)
    {
        var all = await addons.ListAsync(ct);
        var addon = AddonDefinition.Create(AddonId.New(), r.Name, r.Description, r.PriceOnRequest, Prices(r), r.QuantityApplies, r.Capacity,
            r.CapacityUnitLabel, all.Count == 0 ? 0 : all.Max(a => a.SortOrder) + 1, r.IsActive);
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    public async Task<AddonDto> UpdateAsync(string id, SaveAddonRequest r, CancellationToken ct = default)
    {
        var addon = await LoadAsync(id, ct);
        addon.EnsureVersion(r.Version);
        addon.Update(r.Name, r.Description, r.PriceOnRequest, Prices(r), r.QuantityApplies, r.Capacity, r.CapacityUnitLabel, r.IsActive);
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    /// <summary>Deactivates the add-on; it keeps its place.</summary>
    public async Task<AddonDto> DeactivateAsync(string id, CancellationToken ct = default)
    {
        var addon = await LoadAsync(id, ct);
        addon.Deactivate();
        await addons.SaveAsync(addon, ct);
        return ToDto(addon);
    }

    /// <summary>Rewrites <c>SortOrder</c> from the full ordered list of ids.</summary>
    public async Task<IReadOnlyList<AddonDto>> ReorderAsync(AddonOrderRequest request, CancellationToken ct = default)
    {
        var all = await addons.ListAsync(ct);
        var ids = request.AddonIds ?? [];
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || all.Any(a => !ids.Contains(a.Id.Value)))
        {
            throw new DomainValidationException("addonIds", "Send every add-on exactly once.");
        }

        var ordered = ids.Select((id, index) =>
        {
            var addon = all.Single(a => a.Id.Value == id);
            addon.MoveTo(index);
            return addon;
        }).ToList();
        await addons.SaveAllAsync(ordered, ct);
        return ordered.Select(ToDto).ToList();
    }

    private static AddonPrices Prices(SaveAddonRequest r) => new(r.MidweekPrice, r.WeekendPrice, r.WeekPrice);

    private static AddonDto ToDto(AddonDefinition a) =>
        new(a.Id.Value, a.Name, a.Description, a.PriceOnRequest, a.Prices.Midweek, a.Prices.Weekend, a.Prices.Week, a.QuantityApplies, a.Capacity,
            a.CapacityUnitLabel, a.IsActive, a.Version);

    private async Task<AddonDefinition> LoadAsync(string id, CancellationToken ct) =>
        await addons.GetAsync(new AddonId(id), ct) ?? throw new NotFoundException("We couldn't find that add-on.");
}
