using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>Pricing seasons (<c>SeasonsController</c>). Ranges may not overlap; Normal can't be deleted.</summary>
public sealed class SeasonService
{
    private readonly ISeasonRepository seasons;

    public SeasonService(ISeasonRepository seasons)
    {
        this.seasons = seasons;
    }

    /// <summary>Normal first, then by name.</summary>
    public async Task<IReadOnlyList<SeasonDto>> ListAsync(CancellationToken ct = default) =>
        (await seasons.ListAsync(ct)).OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();

    public async Task<SeasonDto> CreateAsync(SaveSeasonRequest request, CancellationToken ct = default)
    {
        var season = Season.Create(SeasonId.New(), request.Name, Ranges(request), await seasons.ListAsync(ct));
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task<SeasonDto> UpdateAsync(string id, SaveSeasonRequest request, CancellationToken ct = default)
    {
        var season = await LoadAsync(id, ct);
        season.EnsureVersion(request.Version);
        season.Update(request.Name, Ranges(request), await seasons.ListAsync(ct));
        await seasons.SaveAsync(season, ct);
        return ToDto(season);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var season = await LoadAsync(id, ct);
        season.EnsureCanDelete();
        await seasons.DeleteAsync(season, ct);
    }

    private static IReadOnlyList<SeasonRange> Ranges(SaveSeasonRequest request) =>
        (request.Ranges ?? []).Select(r => new SeasonRange(DayMonth.Parse(r.Start), DayMonth.Parse(r.End))).ToList();

    private static SeasonDto ToDto(Season s) =>
        new(s.Id.Value, s.Name, s.IsDefault, s.Ranges.Select(r => new SeasonRangeDto(r.Start.ToString(), r.End.ToString())).ToList(), s.Version);

    private async Task<Season> LoadAsync(string id, CancellationToken ct) =>
        await seasons.GetAsync(new SeasonId(id), ct) ?? throw new NotFoundException("We couldn't find that season.");
}
