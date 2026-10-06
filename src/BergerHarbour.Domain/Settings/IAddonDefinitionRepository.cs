using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

public interface IAddonDefinitionRepository
{
    Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default);

    Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default);

    Task SaveAsync(AddonDefinition addon, CancellationToken ct = default);
}
