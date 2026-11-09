using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

public sealed class InMemoryBoatScheduleSession(InMemoryDatabase db, BoatId boatId) : IBoatScheduleSession
{
    private readonly List<Action> writes = [];

    public BoatId BoatId => boatId;

    public Task<Booking?> GetBookingAsync(BookingId id) => Task.FromResult(db.Bookings.Get(id));

    public Task<IReadOnlyList<Booking>> ListOverlappingBookingsAsync(StayRange stay) =>
        Task.FromResult<IReadOnlyList<Booking>>(db.Bookings.All.Where(b => b.BoatId == boatId
            && b.Status is BookingStatus.Active or BookingStatus.PendingPayment && b.Stay.Overlaps(stay)).ToList());

    public Task<IReadOnlyList<BoatUnavailability>> ListOverlappingUnavailabilitiesAsync(StayRange stay) =>
        Task.FromResult<IReadOnlyList<BoatUnavailability>>(db.Unavailabilities.All.Where(u => u.BoatId == boatId && u.Nights.Overlaps(stay)).ToList());

    public Task<BoatUnavailability?> GetUnavailabilityAsync(BoatUnavailabilityId id) => Task.FromResult(db.Unavailabilities.Get(id));

    public void SaveBooking(Booking booking) => writes.Add(() => db.Bookings.Save(booking));

    public void SaveUnavailability(BoatUnavailability unavailability) => writes.Add(() => db.Unavailabilities.Save(unavailability));

    public void DeleteUnavailability(BoatUnavailability unavailability) => writes.Add(() => db.Unavailabilities.Delete(unavailability.Id));

    public void Commit()
    {
        foreach (var write in writes)
        {
            write();
        }
    }
}
