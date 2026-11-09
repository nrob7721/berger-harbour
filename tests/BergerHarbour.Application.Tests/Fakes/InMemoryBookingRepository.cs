using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBookingRepository(InMemoryDatabase db) : IBookingRepository
{
    public Task<Booking?> GetAsync(BookingId id, CancellationToken ct = default) => Task.FromResult(db.Bookings.Get(id));

    public Task SaveAsync(Booking booking, CancellationToken ct = default)
    {
        db.Bookings.Save(booking);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Booking>> ListOverlappingAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Where(b => b.StartDate < to && b.EndDate > from);

    public Task<IReadOnlyList<Booking>> ListWithAddonAsync(AddonId addonId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Where(b => b.StartDate < to && b.EndDate > from && b.Status is BookingStatus.Active or BookingStatus.PendingPayment
            && b.AddonLines.Any(l => l.AddonId == addonId));

    public Task<IReadOnlyList<Booking>> ListByCustomerAsync(CustomerId customerId, CancellationToken ct = default) => Where(b => b.CustomerId == customerId);

    public Task<IReadOnlyList<Booking>> ListByStatusAsync(BookingStatus status, CancellationToken ct = default) => Where(b => b.Status == status);

    public Task<IReadOnlyList<Booking>> ListActiveStartingFromAsync(DateOnly fromStartDate, CancellationToken ct = default) =>
        Where(b => b.Status == BookingStatus.Active && b.StartDate >= fromStartDate);

    public Task<Booking?> GetByPaymentLinkTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(db.Bookings.All.FirstOrDefault(b => b.PaymentLinkTokenHash == tokenHash));

    public Task<IReadOnlyList<Booking>> ListAllAsync(CancellationToken ct = default) => Task.FromResult(db.Bookings.All);

    private Task<IReadOnlyList<Booking>> Where(Func<Booking, bool> predicate) =>
        Task.FromResult<IReadOnlyList<Booking>>(db.Bookings.All.Where(predicate).ToList());
}
