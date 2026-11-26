using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Boats.Dtos;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Boats;

/// <summary>
/// The booking app's More details data (public <c>BoatDetailsController</c>, §8.1), fetched in the background once the boats have
/// loaded. The whole fleet comes in one call, never one call per boat.
/// </summary>
public sealed class BoatDetailsService
{
    private readonly IBoatRepository boats;
    private readonly ReferenceDataLoader referenceData;

    public BoatDetailsService(IBoatRepository boats, ReferenceDataLoader referenceData)
    {
        this.boats = boats;
        this.referenceData = referenceData;
    }

    public async Task<FleetDetailsResponse> ListAsync(CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        var active = Boat.InListOrder((await boats.ListAsync(ct)).Where(b => b.IsActive && b.Type == BoatType.House), reference.Seasons)
            .Select(b => new FleetBoatDetails(b.Slug, Images(b), b.BeddingDescription, b.SecurityBond, b.EffectiveDeposit(reference.Settings), Rates(b)))
            .ToList();
        return new FleetDetailsResponse(active, InTableOrder(reference.Seasons).Select(ToPublic).ToList());
    }

    internal static IReadOnlyList<BoatImageDto> Images(Boat boat) => boat.Images.Select(i => new BoatImageDto(i.Url, i.Description)).ToList();

    internal static IReadOnlyList<BoatRateDto> Rates(Boat boat) =>
        boat.Rates.Select(r => new BoatRateDto(r.SeasonId.Value, r.PeriodType, r.Price)).ToList();

    /// <summary>Normal first, then the other seasons by their earliest range start, then name.</summary>
    private static IEnumerable<Season> InTableOrder(IEnumerable<Season> seasons) =>
        seasons
            .OrderBy(s => !s.IsDefault)
            .ThenBy(s => s.Ranges.Count == 0 ? null : s.Ranges.Min(r => r.Start))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

    private static PublicSeason ToPublic(Season s) =>
        new(s.Id.Value, s.Name, s.Ranges.Select(r => new SeasonRangeDto(r.Start.ToString(), r.End.ToString())).ToList());
}
