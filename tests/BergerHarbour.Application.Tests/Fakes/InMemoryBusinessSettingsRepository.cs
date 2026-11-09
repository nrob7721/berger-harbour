using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBusinessSettingsRepository(InMemoryDatabase db) : IBusinessSettingsRepository
{
    public Task<BusinessSettings?> GetAsync(CancellationToken ct = default) => Task.FromResult(db.Settings.Get("business"));

    public Task SaveAsync(BusinessSettings settings, CancellationToken ct = default)
    {
        db.Settings.Save(settings);
        return Task.CompletedTask;
    }
}
