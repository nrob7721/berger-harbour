using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings;

/// <summary>The settings aggregates most booking rules need.</summary>
public sealed record ReferenceData(
    BusinessSettings Settings,
    IReadOnlyList<Season> Seasons,
    IReadOnlyList<BlockedPeriod> BlockedPeriods,
    IReadOnlyList<AddonDefinition> Addons)
{
    public AddonDefinition? Addon(AddonId id) => Addons.FirstOrDefault(a => a.Id == id);
}
