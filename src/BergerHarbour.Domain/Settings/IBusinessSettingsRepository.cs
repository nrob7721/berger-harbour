namespace BergerHarbour.Domain.Settings;

public interface IBusinessSettingsRepository
{
    /// <summary>The singleton, or null before the seed has run.</summary>
    Task<BusinessSettings?> GetAsync(CancellationToken ct = default);

    Task SaveAsync(BusinessSettings settings, CancellationToken ct = default);
}
