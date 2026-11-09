using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>In-memory aggregates with the same optimistic version check as Firestore.</summary>
public sealed class InMemoryTable<T>(Func<T, string> key, IClock clock) where T : AggregateRoot
{
    private readonly Dictionary<string, T> _items = new();
    public IReadOnlyList<T> All => _items.Values.ToList();

    public T? Get(string id) => _items.GetValueOrDefault(id);

    public void Save(T item)
    {
        var id = key(item);
        var storedVersion = _items.TryGetValue(id, out var existing) ? existing.Version : 0;
        if (storedVersion != item.Version) throw new ConcurrencyConflictException();
        item.MarkPersisted(clock.UtcNow);
        _items[id] = item;
    }

    public void Delete(string id) => _items.Remove(id);
}

public sealed class InMemoryDb(IClock clock) :
    IBoatRepository, IBookingRepository, IBookingDraftRepository, ICustomerRepository, IBoatUnavailabilityRepository,
    ISeasonRepository, IBlockedPeriodRepository, IAddonDefinitionRepository, IEmailTemplateRepository, IBusinessSettingsRepository
{
    public readonly InMemoryTable<Boat> Boats = new(b => b.Id.Value, clock);
    public readonly InMemoryTable<Booking> Bookings = new(b => b.Id, clock);
    public readonly InMemoryTable<BookingDraft> Drafts = new(d => d.Id, clock);
    public readonly InMemoryTable<Customer> Customers = new(c => c.Id, clock);
    public readonly InMemoryTable<BoatUnavailability> Unavailabilities = new(u => u.Id, clock);
    public readonly InMemoryTable<Season> Seasons = new(s => s.Id, clock);
    public readonly InMemoryTable<BlockedPeriod> BlockedPeriods = new(p => p.Id, clock);
    public readonly InMemoryTable<AddonDefinition> Addons = new(a => a.Id, clock);
    public readonly InMemoryTable<EmailTemplate> Templates = new(t => t.Key.ToString(), clock);
    public readonly InMemoryTable<BusinessSettings> Settings = new(_ => "business", clock);

    private static Task<IReadOnlyList<T>> L<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToList());

    // Boats
    Task<IReadOnlyList<Boat>> IBoatRepository.ListAsync(CancellationToken ct) => L(Boats.All);
    public Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default) => Task.FromResult(Boats.Get(id.Value));
    public Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default) => Task.FromResult(Boats.All.FirstOrDefault(b => b.Slug == slug));
    public Task SaveAsync(Boat boat, CancellationToken ct = default) { Boats.Save(boat); return Task.CompletedTask; }

    // Bookings
    Task<Booking?> IBookingRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Bookings.Get(id));
    public Task SaveAsync(Booking booking, CancellationToken ct = default) { Bookings.Save(booking); return Task.CompletedTask; }
    public Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, IReadOnlyCollection<BookingStatus> statuses, CancellationToken ct = default) =>
        L(Bookings.All.Where(b => statuses.Contains(b.Status) && b.StartDate < to && b.EndDate > from));
    public Task<IReadOnlyList<Booking>> ListWithAddonAsync(string addonId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        L(Bookings.All.Where(b => b.AddonIds.Contains(addonId) && BookingStatuses.Blocking.Contains(b.Status) && b.StartDate < to && b.EndDate > from));
    public Task<IReadOnlyList<Booking>> ListByCustomerAsync(string customerId, CancellationToken ct = default) => L(Bookings.All.Where(b => b.CustomerId == customerId));
    public Task<IReadOnlyList<Booking>> ListByCustomersAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
        L(Bookings.All.Where(b => b.CustomerId is not null && ids.Contains(b.CustomerId)));
    public Task<IReadOnlyList<Booking>> ListWithoutCustomerAsync(CancellationToken ct = default) => L(Bookings.All.Where(b => b.CustomerId is null));
    public Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) => L(Bookings.All.Where(b => b.Status == status));
    public Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly from, CancellationToken ct = default) =>
        L(Bookings.All.Where(b => b.Status == BookingStatus.Active && b.StartDate >= from));
    public Task<IReadOnlyList<Booking>> ListByReferencePrefixAsync(string upperPrefix, CancellationToken ct = default) =>
        L(Bookings.All.Where(b => b.Reference?.StartsWith(upperPrefix, StringComparison.Ordinal) == true));
    public Task<Booking?> GetByPaymentLinkHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Bookings.All.FirstOrDefault(b => b.PaymentLinkTokenHash == tokenHash));

    // Drafts
    Task<BookingDraft?> IBookingDraftRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Drafts.Get(id));
    Task<IReadOnlyList<BookingDraft>> IBookingDraftRepository.ListAsync(CancellationToken ct) => L(Drafts.All);
    public Task<BookingDraft?> GetLatestForEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Drafts.All.Where(d => d.Email == email).MaxBy(d => d.LastActivityAt));
    public Task SaveAsync(BookingDraft draft, CancellationToken ct = default) { Drafts.Save(draft); return Task.CompletedTask; }
    public Task DeleteAsync(BookingDraft draft, CancellationToken ct = default) { Drafts.Delete(draft.Id); return Task.CompletedTask; }

    // Customers (email unique)
    Task<IReadOnlyList<Customer>> ICustomerRepository.ListAsync(CancellationToken ct) => L(Customers.All);
    Task<Customer?> ICustomerRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Customers.Get(id));
    public Task<IReadOnlyList<Customer>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) => L(Customers.All.Where(c => ids.Contains(c.Id)));
    public Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(Customers.All.FirstOrDefault(c => c.Email == email));
    public Task SaveAsync(Customer customer, CancellationToken ct = default)
    {
        if (Customers.All.Any(c => c.Email == customer.Email && c.Id != customer.Id)) throw new ConflictException("Another customer already uses this email.");
        Customers.Save(customer);
        return Task.CompletedTask;
    }

    // Unavailabilities
    Task<BoatUnavailability?> IBoatUnavailabilityRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Unavailabilities.Get(id));
    public Task<IReadOnlyList<BoatUnavailability>> ListForBoatAsync(BoatId boatId, CancellationToken ct = default) => L(Unavailabilities.All.Where(u => u.BoatId == boatId));
    public Task<IReadOnlyList<BoatUnavailability>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        L(Unavailabilities.All.Where(u => u.Nights.FirstNight < to && u.Nights.LastNight >= from));
    public Task DeleteAsync(BoatUnavailability u, CancellationToken ct = default) { Unavailabilities.Delete(u.Id); return Task.CompletedTask; }

    // Seasons
    Task<IReadOnlyList<Season>> ISeasonRepository.ListAsync(CancellationToken ct) => L(Seasons.All);
    Task<Season?> ISeasonRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Seasons.Get(id));
    public Task SaveAsync(Season season, CancellationToken ct = default) { Seasons.Save(season); return Task.CompletedTask; }
    public Task DeleteAsync(Season season, CancellationToken ct = default) { Seasons.Delete(season.Id); return Task.CompletedTask; }

    // Blocked periods
    Task<IReadOnlyList<BlockedPeriod>> IBlockedPeriodRepository.ListAsync(CancellationToken ct) => L(BlockedPeriods.All);
    Task<BlockedPeriod?> IBlockedPeriodRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(BlockedPeriods.Get(id));
    public Task SaveAsync(BlockedPeriod period, CancellationToken ct = default) { BlockedPeriods.Save(period); return Task.CompletedTask; }
    public Task DeleteAsync(BlockedPeriod period, CancellationToken ct = default) { BlockedPeriods.Delete(period.Id); return Task.CompletedTask; }

    // Add-ons
    Task<IReadOnlyList<AddonDefinition>> IAddonDefinitionRepository.ListAsync(CancellationToken ct) => L(Addons.All.OrderBy(a => a.SortOrder));
    Task<AddonDefinition?> IAddonDefinitionRepository.GetAsync(string id, CancellationToken ct) => Task.FromResult(Addons.Get(id));
    public Task SaveAsync(AddonDefinition addon, CancellationToken ct = default) { Addons.Save(addon); return Task.CompletedTask; }
    public Task SaveAllAsync(IEnumerable<AddonDefinition> addons, CancellationToken ct = default) { foreach (var a in addons) Addons.Save(a); return Task.CompletedTask; }

    // Templates
    Task<IReadOnlyList<EmailTemplate>> IEmailTemplateRepository.ListAsync(CancellationToken ct) => L(Templates.All);
    public Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default) => Task.FromResult(Templates.Get(key.ToString()));
    public Task SaveAsync(EmailTemplate template, CancellationToken ct = default) { Templates.Save(template); return Task.CompletedTask; }

    // Settings
    Task<BusinessSettings?> IBusinessSettingsRepository.GetAsync(CancellationToken ct) => Task.FromResult(Settings.Get("business"));
    public Task SaveAsync(BusinessSettings settings, CancellationToken ct = default) { Settings.Save(settings); return Task.CompletedTask; }
}

/// <summary>Runs the work against the in-memory db, committing buffered writes only when it returns.</summary>
public sealed class InMemoryScheduleLock(InMemoryDb db) : IBoatScheduleLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = new Session(db, boatId);
            var result = await work(session);
            foreach (var b in session.Bookings) db.Bookings.Save(b);
            foreach (var u in session.Unavailabilities) db.Unavailabilities.Save(u);
            return result;
        }
        finally { _gate.Release(); }
    }

    private sealed class Session(InMemoryDb db, BoatId boatId) : IBoatScheduleSession
    {
        public readonly List<Booking> Bookings = [];
        public readonly List<BoatUnavailability> Unavailabilities = [];
        public BoatId BoatId => boatId;

        public Task<IReadOnlyList<Booking>> GetPotentiallyOverlappingBookingsAsync(StayRange stay) => Task.FromResult<IReadOnlyList<Booking>>(
            db.Bookings.All.Where(b => b.BoatId == boatId && BookingStatuses.Blocking.Contains(b.Status) && b.StartDate < stay.End && b.EndDate > stay.Start).ToList());
        public Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(StayRange stay) => Task.FromResult<IReadOnlyList<BoatUnavailability>>(
            db.Unavailabilities.All.Where(u => u.BoatId == boatId && stay.Overlaps(u.Nights)).ToList());
        public Task<Booking?> GetBookingAsync(string id) => Task.FromResult(db.Bookings.Get(id));
        public Task<BoatUnavailability?> GetUnavailabilityAsync(string id) => Task.FromResult(db.Unavailabilities.Get(id));
        public void SaveBooking(Booking booking) { if (!Bookings.Contains(booking)) Bookings.Add(booking); }
        public void SaveUnavailability(BoatUnavailability u) { if (!Unavailabilities.Contains(u)) Unavailabilities.Add(u); }
    }
}

public sealed class FakeReferenceGenerator : IBookingReferenceGenerator
{
    public readonly Dictionary<string, int> Counters = new();

    public Task<int> NextSuffixAsync(string prefix, CancellationToken ct = default)
    {
        var next = Counters.GetValueOrDefault(prefix);
        Counters[prefix] = next + 1;
        return Task.FromResult(next);
    }
}

public sealed class FakePaymentGateway : IPaymentGateway
{
    public readonly List<CheckoutRequest> Created = [];
    public readonly Dictionary<string, string> SessionStatus = new();
    public readonly List<string> Expired = [];
    public bool Fail { get; set; }

    public Task<CheckoutSession> CreateEmbeddedCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("Stripe is down");
        Created.Add(request);
        var id = $"cs_test_{Created.Count}";
        SessionStatus[id] = "open";
        return Task.FromResult(new CheckoutSession(id, id + "_secret"));
    }

    public Task<ExpireSessionResult> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var status = SessionStatus.GetValueOrDefault(sessionId, "open");
        if (status == "complete") return Task.FromResult(ExpireSessionResult.AlreadyCompleted);
        if (status == "expired") return Task.FromResult(ExpireSessionResult.AlreadyExpired);
        SessionStatus[sessionId] = "expired";
        Expired.Add(sessionId);
        return Task.FromResult(ExpireSessionResult.Expired);
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public readonly List<EmailMessage> Sent = [];

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> WithSubject(string contains) => Sent.Where(m => m.Subject.Contains(contains, StringComparison.Ordinal)).ToList();
}

public sealed class FakeTurnstile : ITurnstileVerifier
{
    public bool Pass { get; set; } = true;
    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) => Task.FromResult(Pass && token == "ok");
}
