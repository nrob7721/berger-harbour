using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

public sealed class FirestoreBoatRepository(FirestoreStore store) : IBoatRepository
{
    private static Boat Map(BoatDoc d, string id) => d.ToDomain(id);

    public Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BoatDoc, Boat>(store.Collection(Collections.Boats), Map, ct);

    public Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default) =>
        store.GetAsync<BoatDoc, Boat>(Collections.Boats, id.Value, Map, ct);

    public async Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        (await FirestoreStore.QueryAsync<BoatDoc, Boat>(store.Collection(Collections.Boats).WhereEqualTo("slug", slug).Limit(1), Map, ct)).FirstOrDefault();

    public Task SaveAsync(Boat boat, CancellationToken ct = default) =>
        store.SaveAsync(Collections.Boats, boat.Id.Value, boat, () => BoatDoc.From(boat), ct);
}

public sealed class FirestoreCustomerRepository(FirestoreStore store) : ICustomerRepository
{
    private static Customer Map(CustomerDoc d, string id) => d.ToDomain(id);

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<CustomerDoc, Customer>(store.Collection(Collections.Customers), Map, ct);

    public Task<Customer?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<CustomerDoc, Customer>(Collections.Customers, id, Map, ct);

    public async Task<IReadOnlyList<Customer>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        var refs = ids.Distinct().Select(id => store.Collection(Collections.Customers).Document(id)).ToList();
        var snaps = await store.Db.GetAllSnapshotsAsync(refs, ct);
        return snaps.Where(s => s.Exists).Select(s => s.ConvertTo<CustomerDoc>().ToDomain(s.Id)).ToList();
    }

    public async Task<Customer?> GetByEmailAsync(string normalisedEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(normalisedEmail)) return null;
        var index = await store.Collection(Collections.CustomerEmails).Document(EmailKey(normalisedEmail)).GetSnapshotAsync(ct);
        if (!index.Exists) return null;
        return await GetAsync(index.GetValue<string>("customerId"), ct);
    }

    /// <summary>Document ids can't contain '/'.</summary>
    internal static string EmailKey(string normalisedEmail) => normalisedEmail.Replace("/", "%2F");

    /// <summary>Saves the customer and its <c>customerEmails/{email}</c> index document in one transaction, so emails stay unique.</summary>
    public Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        var indexRef = store.Collection(Collections.CustomerEmails).Document(EmailKey(customer.Email));
        return store.SaveAsync(Collections.Customers, customer.Id, customer, () => CustomerDoc.From(customer), ct,
            alsoRead: async tx =>
            {
                var index = await tx.GetSnapshotAsync(indexRef, ct);
                if (index.Exists && index.GetValue<string>("customerId") != customer.Id)
                    throw new ConflictException("Another customer already uses this email.");
            },
            alsoWrite: tx => tx.Set(indexRef, new Dictionary<string, object> { ["customerId"] = customer.Id }));
    }
}

public sealed class FirestoreBookingRepository(FirestoreStore store) : IBookingRepository
{
    /// <summary>The longest stay a range query looks back for. Stays are at most a few weeks.</summary>
    internal const int MaxStayNights = 120;

    private static Booking Map(BookingDoc d, string id) => d.ToDomain(id);
    private CollectionReference Bookings => store.Collection(Collections.Bookings);

    public Task<Booking?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<BookingDoc, Booking>(Collections.Bookings, id, Map, ct);

    public Task SaveAsync(Booking booking, CancellationToken ct = default) =>
        store.SaveAsync(Collections.Bookings, booking.Id, booking, () => BookingDoc.From(booking), ct);

    public async Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, IReadOnlyCollection<BookingStatus> statuses, CancellationToken ct = default)
    {
        var query = Bookings
            .WhereIn("status", statuses.Select(s => s.ToString()).ToArray())
            .WhereGreaterThanOrEqualTo("startDate", Conv.D(from.AddDays(-MaxStayNights)))
            .WhereLessThan("startDate", Conv.D(to));
        return (await FirestoreStore.QueryAsync<BookingDoc, Booking>(query, Map, ct)).Where(b => b.EndDate > from).ToList();
    }

    public async Task<IReadOnlyList<Booking>> ListWithAddonAsync(string addonId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var query = Bookings
            .WhereArrayContains("addonIds", addonId)
            .WhereIn("status", BookingStatuses.Blocking.Select(s => s.ToString()).ToArray())
            .WhereGreaterThanOrEqualTo("startDate", Conv.D(from.AddDays(-MaxStayNights)))
            .WhereLessThan("startDate", Conv.D(to));
        return (await FirestoreStore.QueryAsync<BookingDoc, Booking>(query, Map, ct)).Where(b => b.EndDate > from).ToList();
    }

    public Task<IReadOnlyList<Booking>> ListByCustomerAsync(string customerId, CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDoc, Booking>(Bookings.WhereEqualTo("customerId", customerId), Map, ct);

    public async Task<IReadOnlyList<Booking>> ListByCustomersAsync(IReadOnlyCollection<string> customerIds, CancellationToken ct = default)
    {
        var result = new List<Booking>();
        foreach (var chunk in customerIds.Distinct().Chunk(30))
            result.AddRange(await FirestoreStore.QueryAsync<BookingDoc, Booking>(Bookings.WhereIn("customerId", chunk), Map, ct));
        return result;
    }

    public Task<IReadOnlyList<Booking>> ListWithoutCustomerAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDoc, Booking>(Bookings.WhereEqualTo("customerId", null), Map, ct);

    public Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDoc, Booking>(Bookings.WhereEqualTo("status", status.ToString()), Map, ct);

    public Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly from, CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDoc, Booking>(
            Bookings.WhereEqualTo("status", nameof(BookingStatus.Active)).WhereGreaterThanOrEqualTo("startDate", Conv.D(from)), Map, ct);

    public Task<IReadOnlyList<Booking>> ListByReferencePrefixAsync(string upperPrefix, CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDoc, Booking>(
            Bookings.WhereGreaterThanOrEqualTo("reference", upperPrefix).WhereLessThan("reference", upperPrefix + "").Limit(50), Map, ct);

    public async Task<Booking?> GetByPaymentLinkHashAsync(string tokenHash, CancellationToken ct = default) =>
        (await FirestoreStore.QueryAsync<BookingDoc, Booking>(Bookings.WhereEqualTo("paymentLinkTokenHash", tokenHash).Limit(1), Map, ct)).FirstOrDefault();
}

public sealed class FirestoreBoatUnavailabilityRepository(FirestoreStore store) : IBoatUnavailabilityRepository
{
    private static BoatUnavailability Map(UnavailabilityDoc d, string id) => d.ToDomain(id);

    public Task<BoatUnavailability?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<UnavailabilityDoc, BoatUnavailability>(Collections.BoatUnavailabilities, id, Map, ct);

    public Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<UnavailabilityDoc, BoatUnavailability>(
            store.Collection(Collections.BoatUnavailabilities).WhereEqualTo("boatId", boatId.Value), Map, ct);

    public async Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var query = store.Collection(Collections.BoatUnavailabilities).WhereLessThan("firstNight", Conv.D(to));
        return (await FirestoreStore.QueryAsync<UnavailabilityDoc, BoatUnavailability>(query, Map, ct))
            .Where(u => u.Nights.LastNight >= from).ToList();
    }

    public Task DeleteAsync(BoatUnavailability unavailability, CancellationToken ct = default) =>
        store.DeleteAsync(Collections.BoatUnavailabilities, unavailability.Id, ct);
}

public sealed class FirestoreBookingDraftRepository(FirestoreStore store) : IBookingDraftRepository
{
    private static BookingDraft Map(BookingDraftDoc d, string id) => d.ToDomain(id);

    public Task<BookingDraft?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<BookingDraftDoc, BookingDraft>(Collections.BookingDrafts, id, Map, ct);

    public Task<IReadOnlyList<BookingDraft>> ListAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BookingDraftDoc, BookingDraft>(store.Collection(Collections.BookingDrafts), Map, ct);

    public async Task<BookingDraft?> GetLatestForEmailAsync(string normalisedEmail, CancellationToken ct = default) =>
        (await FirestoreStore.QueryAsync<BookingDraftDoc, BookingDraft>(store.Collection(Collections.BookingDrafts)
            .WhereEqualTo("email", normalisedEmail).OrderByDescending("lastActivityAt").Limit(1), Map, ct)).FirstOrDefault();

    public Task SaveAsync(BookingDraft draft, CancellationToken ct = default) =>
        store.SaveAsync(Collections.BookingDrafts, draft.Id, draft, () => BookingDraftDoc.From(draft), ct);

    public Task DeleteAsync(BookingDraft draft, CancellationToken ct = default) => store.DeleteAsync(Collections.BookingDrafts, draft.Id, ct);
}

public sealed class FirestoreSeasonRepository(FirestoreStore store) : ISeasonRepository
{
    private static Season Map(SeasonDoc d, string id) => d.ToDomain(id);

    public Task<IReadOnlyList<Season>> ListAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<SeasonDoc, Season>(store.Collection(Collections.Seasons), Map, ct);

    public Task<Season?> GetAsync(string id, CancellationToken ct = default) => store.GetAsync<SeasonDoc, Season>(Collections.Seasons, id, Map, ct);

    public Task SaveAsync(Season season, CancellationToken ct = default) =>
        store.SaveAsync(Collections.Seasons, season.Id, season, () => SeasonDoc.From(season), ct);

    public Task DeleteAsync(Season season, CancellationToken ct = default) => store.DeleteAsync(Collections.Seasons, season.Id, ct);
}

public sealed class FirestoreBlockedPeriodRepository(FirestoreStore store) : IBlockedPeriodRepository
{
    private static BlockedPeriod Map(BlockedPeriodDoc d, string id) => d.ToDomain(id);

    public Task<IReadOnlyList<BlockedPeriod>> ListAsync(CancellationToken ct = default) =>
        FirestoreStore.QueryAsync<BlockedPeriodDoc, BlockedPeriod>(store.Collection(Collections.BlockedPeriods), Map, ct);

    public Task<BlockedPeriod?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<BlockedPeriodDoc, BlockedPeriod>(Collections.BlockedPeriods, id, Map, ct);

    public Task SaveAsync(BlockedPeriod period, CancellationToken ct = default) =>
        store.SaveAsync(Collections.BlockedPeriods, period.Id, period, () => BlockedPeriodDoc.From(period), ct);

    public Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default) => store.DeleteAsync(Collections.BlockedPeriods, period.Id, ct);
}

public sealed class FirestoreAddonDefinitionRepository(FirestoreStore store) : IAddonDefinitionRepository
{
    private static AddonDefinition Map(AddonDoc d, string id) => d.ToDomain(id);

    public async Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default) =>
        (await FirestoreStore.QueryAsync<AddonDoc, AddonDefinition>(store.Collection(Collections.Addons), Map, ct)).OrderBy(a => a.SortOrder).ToList();

    public Task<AddonDefinition?> GetAsync(string id, CancellationToken ct = default) =>
        store.GetAsync<AddonDoc, AddonDefinition>(Collections.Addons, id, Map, ct);

    public Task SaveAsync(AddonDefinition addon, CancellationToken ct = default) =>
        store.SaveAsync(Collections.Addons, addon.Id, addon, () => AddonDoc.From(addon), ct);

    public async Task SaveAllAsync(IEnumerable<AddonDefinition> addons, CancellationToken ct = default)
    {
        foreach (var a in addons) await SaveAsync(a, ct);
    }
}

public sealed class FirestoreEmailTemplateRepository(FirestoreStore store) : IEmailTemplateRepository
{
    private static EmailTemplate Map(EmailTemplateDoc d, string id) => d.ToDomain(id);

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        (await FirestoreStore.QueryAsync<EmailTemplateDoc, EmailTemplate?>(store.Collection(Collections.EmailTemplates),
            (d, id) => Enum.TryParse<EmailTemplateKey>(id, out _) ? Map(d, id) : null, ct)).OfType<EmailTemplate>().ToList();

    public Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default) =>
        store.GetAsync<EmailTemplateDoc, EmailTemplate>(Collections.EmailTemplates, key.ToString(), Map, ct);

    public Task SaveAsync(EmailTemplate template, CancellationToken ct = default) =>
        store.SaveAsync(Collections.EmailTemplates, template.Key.ToString(), template, () => EmailTemplateDoc.From(template), ct);
}

public sealed class FirestoreBusinessSettingsRepository(FirestoreStore store) : IBusinessSettingsRepository
{
    private const string Id = "business";

    public Task<BusinessSettings?> GetAsync(CancellationToken ct = default) =>
        store.GetAsync<BusinessSettingsDoc, BusinessSettings>(Collections.Settings, Id, (d, _) => d.ToDomain(), ct);

    public Task SaveAsync(BusinessSettings settings, CancellationToken ct = default) =>
        store.SaveAsync(Collections.Settings, Id, settings, () => BusinessSettingsDoc.From(settings), ct);
}
