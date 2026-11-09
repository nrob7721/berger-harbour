using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemorySeasonRepository(InMemoryDatabase db) : ISeasonRepository
{
    public Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default) => Task.FromResult(db.Seasons.All);

    public Task<Season?> GetAsync(SeasonId id, CancellationToken ct = default) => Task.FromResult(db.Seasons.Get(id));

    public Task SaveAsync(Season season, CancellationToken ct = default)
    {
        db.Seasons.Save(season);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Season season, CancellationToken ct = default)
    {
        db.Seasons.Delete(season.Id);
        return Task.CompletedTask;
    }
}
