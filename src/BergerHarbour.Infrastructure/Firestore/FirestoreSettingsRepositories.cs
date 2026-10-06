using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;
using static BergerHarbour.Infrastructure.Firestore.FirestoreMapping;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreSeasonRepository(FirestoreDb db) : ISeasonRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.Seasons);

    public async Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default) =>
        (await Collection.GetSnapshotAsync(ct)).Documents.Select(d => ToSeason(d.Id, d.ConvertTo<SeasonDocument>())).ToList();

    public async Task<Season?> GetAsync(SeasonId id, CancellationToken ct = default)
    {
        var s = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return s.Exists ? ToSeason(s.Id, s.ConvertTo<SeasonDocument>()) : null;
    }

    public async Task SaveAsync(Season season, CancellationToken ct = default)
    {
        var s = season.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        season.MarkPersisted();
    }

    public Task DeleteAsync(Season season, CancellationToken ct = default) =>
        VersionedWriter.DeleteAsync(db, Collection.Document(season.Id.Value), season.Version, ct);
}

public sealed class FirestoreBlockedPeriodRepository(FirestoreDb db) : IBlockedPeriodRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.BlockedPeriods);

    public async Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default) =>
        (await Collection.GetSnapshotAsync(ct)).Documents.Select(d => ToBlockedPeriod(d.Id, d.ConvertTo<BlockedPeriodDocument>())).ToList();

    public async Task<BlockedPeriod?> GetAsync(BlockedPeriodId id, CancellationToken ct = default)
    {
        var s = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return s.Exists ? ToBlockedPeriod(s.Id, s.ConvertTo<BlockedPeriodDocument>()) : null;
    }

    public async Task SaveAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default)
    {
        var s = blockedPeriod.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        blockedPeriod.MarkPersisted();
    }

    public Task DeleteAsync(BlockedPeriod blockedPeriod, CancellationToken ct = default) =>
        VersionedWriter.DeleteAsync(db, Collection.Document(blockedPeriod.Id.Value), blockedPeriod.Version, ct);
}

public sealed class FirestoreAddonDefinitionRepository(FirestoreDb db) : IAddonDefinitionRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.Addons);

    public async Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default) =>
        (await Collection.GetSnapshotAsync(ct)).Documents.Select(d => ToAddon(d.Id, d.ConvertTo<AddonDocument>())).ToList();

    public async Task<AddonDefinition?> GetAsync(AddonId id, CancellationToken ct = default)
    {
        var s = await Collection.Document(id.Value).GetSnapshotAsync(ct);
        return s.Exists ? ToAddon(s.Id, s.ConvertTo<AddonDocument>()) : null;
    }

    public async Task SaveAsync(AddonDefinition addon, CancellationToken ct = default)
    {
        var s = addon.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Id), s.Version, ToDocument(s, s.Version + 1), ct);
        addon.MarkPersisted();
    }
}

public sealed class FirestoreEmailTemplateRepository(FirestoreDb db) : IEmailTemplateRepository
{
    private CollectionReference Collection => db.Collection(FirestoreCollections.EmailTemplates);

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        (await Collection.GetSnapshotAsync(ct)).Documents
        .Where(d => Enum.TryParse<EmailTemplateKey>(d.Id, out _))
        .Select(d => ToTemplate(d.Id, d.ConvertTo<EmailTemplateDocument>())).ToList();

    public async Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default)
    {
        var s = await Collection.Document(key.ToString()).GetSnapshotAsync(ct);
        return s.Exists ? ToTemplate(s.Id, s.ConvertTo<EmailTemplateDocument>()) : null;
    }

    public async Task SaveAsync(EmailTemplate template, CancellationToken ct = default)
    {
        var s = template.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Collection.Document(s.Key.ToString()), s.Version, ToDocument(s, s.Version + 1), ct);
        template.MarkPersisted();
    }
}

public sealed class FirestoreBusinessSettingsRepository(FirestoreDb db) : IBusinessSettingsRepository
{
    private DocumentReference Document => db.Collection(FirestoreCollections.Settings).Document(BusinessSettings.SingletonId);

    public async Task<BusinessSettings> GetAsync(CancellationToken ct = default) =>
        await FindAsync(ct) ?? throw new InvalidOperationException("Business settings are missing. Run the seed.");

    public async Task<BusinessSettings?> FindAsync(CancellationToken ct = default)
    {
        var s = await Document.GetSnapshotAsync(ct);
        return s.Exists ? ToBusinessSettings(s.ConvertTo<BusinessSettingsDocument>()) : null;
    }

    public async Task SaveAsync(BusinessSettings settings, CancellationToken ct = default)
    {
        var s = settings.ToSnapshot();
        await VersionedWriter.SaveAsync(db, Document, s.Version, ToDocument(s, s.Version + 1), ct);
        settings.MarkPersisted();
    }
}
