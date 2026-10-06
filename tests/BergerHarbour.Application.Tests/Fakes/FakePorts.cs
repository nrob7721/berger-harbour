using System.Collections.Concurrent;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Email provider down");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> WithSubjectContaining(string text) =>
        Sent.Where(m => m.Subject.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
}

public sealed class FakePaymentGateway : IPaymentGateway
{
    public List<CheckoutSessionRequest> Requests { get; } = [];

    public bool Fail { get; set; }

    public Task<CheckoutSessionResult> CreateEmbeddedCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Stripe down");
        }

        Requests.Add(request);
        var id = $"cs_test_{Requests.Count}";
        return Task.FromResult(new CheckoutSessionResult(id, id + "_secret_x"));
    }
}

public sealed class FakeTurnstile : ITurnstileVerifier
{
    public bool Accept { get; set; } = true;

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) =>
        Task.FromResult(Accept && !string.IsNullOrEmpty(token));
}

public sealed class FakeReferenceGenerator : IBookingReferenceGenerator
{
    private int _next;

    public Task<BookingReference> NextAsync(int year, CancellationToken ct = default) =>
        Task.FromResult(BookingReference.Create(year, Interlocked.Increment(ref _next)));
}

/// <summary>Serialises work per boat with a semaphore and applies buffered writes at the end, like the Firestore lock.</summary>
public sealed class InMemoryBoatScheduleLock(InMemoryStore store, InMemoryBookingRepository bookings,
    InMemoryUnavailabilityRepository unavailabilities) : IBoatScheduleLock
{
    private readonly ConcurrentDictionary<BoatId, SemaphoreSlim> _locks = new();

    public async Task<T> RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(boatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var session = new Session(boatId, store);
            var result = await work(session);
            foreach (var booking in session.Bookings)
            {
                bookings.Save(booking);
            }

            foreach (var unavailability in session.Unavailabilities)
            {
                unavailabilities.Save(unavailability);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class Session(BoatId boatId, InMemoryStore store) : IBoatScheduleSession
    {
        private readonly Dictionary<BookingId, Booking> _loaded = new();

        public List<Booking> Bookings { get; } = [];

        public List<BoatUnavailability> Unavailabilities { get; } = [];

        public BoatId BoatId => boatId;

        public Task<Booking?> GetBookingAsync(BookingId id) => Task.FromResult(Track(store.Bookings.Get(id.Value)));

        public Task<IReadOnlyList<Booking>> GetOccupyingBookingsAsync(Stay range) =>
            Task.FromResult<IReadOnlyList<Booking>>(store.Bookings.All()
                .Where(b => b.BoatId == boatId.Value && b.Status is BookingStatus.Active or BookingStatus.PendingPayment &&
                            b.StartDate < range.EndDate && b.EndDate > range.StartDate)
                .Select(Track).OfType<Booking>().ToList());

        public Task<IReadOnlyList<BoatUnavailability>> GetUnavailabilitiesAsync(Stay range) =>
            Task.FromResult<IReadOnlyList<BoatUnavailability>>(store.Unavailabilities.All()
                .Where(u => u.BoatId == boatId.Value && u.FirstNight < range.EndDate && u.LastNight >= range.StartDate)
                .Select(BoatUnavailability.FromSnapshot).ToList());

        public Task<BoatUnavailability?> GetUnavailabilityAsync(UnavailabilityId id) =>
            Task.FromResult(store.Unavailabilities.Get(id.Value) is { } s ? BoatUnavailability.FromSnapshot(s) : null);

        public void Save(Booking booking)
        {
            if (!Bookings.Contains(booking))
            {
                Bookings.Add(booking);
            }
        }

        public void Save(BoatUnavailability unavailability) => Unavailabilities.Add(unavailability);

        private Booking? Track(BookingSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return null;
            }

            var id = new BookingId(snapshot.Id);
            if (!_loaded.TryGetValue(id, out var booking))
            {
                _loaded[id] = booking = Booking.FromSnapshot(snapshot);
            }

            return booking;
        }
    }
}
