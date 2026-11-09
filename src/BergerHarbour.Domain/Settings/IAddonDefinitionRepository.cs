namespace BergerHarbour.Domain.Settings;

public interface IAddonDefinitionRepository
{
    /// <summary>Every add-on, active or not, in the admin-defined order.</summary>
    Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default);

    Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default);

    Task SaveAsync(AddonDefinition addon, CancellationToken ct = default);

    /// <summary>Saves several add-ons in one atomic write (reordering).</summary>
    Task SaveAllAsync(IReadOnlyList<AddonDefinition> addons, CancellationToken ct = default);
}
