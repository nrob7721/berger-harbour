using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryAddonDefinitionRepository(InMemoryDatabase db) : IAddonDefinitionRepository
{
    public Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AddonDefinition>>(db.Addons.All.OrderBy(a => a.SortOrder).ToList());

    public Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default) => Task.FromResult(db.Addons.Get(id));

    public Task SaveAsync(AddonDefinition addon, CancellationToken ct = default)
    {
        db.Addons.Save(addon);
        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyList<AddonDefinition> addons, CancellationToken ct = default)
    {
        foreach (var addon in addons)
        {
            db.Addons.Save(addon);
        }

        return Task.CompletedTask;
    }
}
