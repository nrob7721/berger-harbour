namespace BergerHarbour.Domain.Settings;

public interface IBusinessSettingsRepository
{
    /// <summary>Returns the singleton. Throws when the seed has not been run.</summary>
    Task<BusinessSettings> GetAsync(CancellationToken ct = default);

    Task<BusinessSettings?> FindAsync(CancellationToken ct = default);

    Task SaveAsync(BusinessSettings settings, CancellationToken ct = default);
}
