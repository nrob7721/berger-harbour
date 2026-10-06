using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>Snapshot-based storage with the same version semantics as the Firestore repositories.</summary>
public sealed class VersionedTable<TKey, TSnapshot> where TKey : notnull
{
    private readonly Dictionary<TKey, TSnapshot> _rows = new();
    private readonly object _gate = new();

    public IReadOnlyList<TSnapshot> All()
    {
        lock (_gate)
        {
            return _rows.Values.ToList();
        }
    }

    public TSnapshot? Get(TKey key)
    {
        lock (_gate)
        {
            return _rows.TryGetValue(key, out var row) ? row : default;
        }
    }

    public void Save(TKey key, int loadedVersion, Func<int, TSnapshot> snapshotAtVersion)
    {
        lock (_gate)
        {
            var exists = _rows.ContainsKey(key);
            if (loadedVersion == 0 && exists)
            {
                throw new ConcurrencyConflictException();
            }

            if (loadedVersion > 0 && (!exists || CurrentVersion(key) != loadedVersion))
            {
                throw new ConcurrencyConflictException();
            }

            _rows[key] = snapshotAtVersion(loadedVersion + 1);
        }
    }

    public Func<TKey, int> CurrentVersion { get; set; } = _ => 0;

    public void Remove(TKey key)
    {
        lock (_gate)
        {
            _rows.Remove(key);
        }
    }
}

public sealed class InMemoryStore
{
    public VersionedTable<string, BookingSnapshot> Bookings { get; } = new();
    public VersionedTable<string, BoatSnapshot> Boats { get; } = new();
    public VersionedTable<string, CustomerSnapshot> Customers { get; } = new();
    public VersionedTable<string, BoatUnavailabilitySnapshot> Unavailabilities { get; } = new();
    public VersionedTable<string, SeasonSnapshot> Seasons { get; } = new();
    public VersionedTable<string, BlockedPeriodSnapshot> BlockedPeriods { get; } = new();
    public VersionedTable<string, AddonDefinitionSnapshot> Addons { get; } = new();
    public VersionedTable<EmailTemplateKey, EmailTemplateSnapshot> Templates { get; } = new();
    public VersionedTable<string, BusinessSettingsSnapshot> Business { get; } = new();

    public InMemoryStore()
    {
        Bookings.CurrentVersion = k => Bookings.Get(k)!.Version;
        Boats.CurrentVersion = k => Boats.Get(k)!.Version;
        Customers.CurrentVersion = k => Customers.Get(k)!.Version;
        Unavailabilities.CurrentVersion = k => Unavailabilities.Get(k)!.Version;
        Seasons.CurrentVersion = k => Seasons.Get(k)!.Version;
        BlockedPeriods.CurrentVersion = k => BlockedPeriods.Get(k)!.Version;
        Addons.CurrentVersion = k => Addons.Get(k)!.Version;
        Templates.CurrentVersion = k => Templates.Get(k)!.Version;
        Business.CurrentVersion = k => Business.Get(k)!.Version;
    }
}
