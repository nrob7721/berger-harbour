using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Unavailabilities.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Unavailabilities;

/// <summary>A boat's unavailabilities (<c>BoatUnavailabilitiesController</c>). Every write runs inside the boat's lock.</summary>
public sealed class BoatUnavailabilityService
{
    private readonly IBoatRepository boats;
    private readonly IBoatUnavailabilityRepository unavailabilities;
    private readonly IBoatScheduleLock scheduleLock;
    private readonly BookingAvailabilityService availability;
    private readonly IClock clock;

    public BoatUnavailabilityService(
        IBoatRepository boats, IBoatUnavailabilityRepository unavailabilities, IBoatScheduleLock scheduleLock, BookingAvailabilityService availability,
        IClock clock)
    {
        this.boats = boats;
        this.unavailabilities = unavailabilities;
        this.scheduleLock = scheduleLock;
        this.availability = availability;
        this.clock = clock;
    }

    public async Task<IReadOnlyList<UnavailabilityDto>> ListAsync(string boatId, CancellationToken ct = default)
    {
        var boat = await BoatAsync(boatId, ct);
        return (await unavailabilities.ListForBoatAsync(boat.Id, ct)).OrderBy(u => u.Nights.FirstNight).Select(ToDto).ToList();
    }

    /// <summary>Rejected (409, naming the references) if it overlaps an Active or PendingPayment non-stand-by booking.</summary>
    public async Task<UnavailabilityDto> CreateAsync(string boatId, SaveUnavailabilityRequest request, CancellationToken ct = default)
    {
        var boat = await BoatAsync(boatId, ct);
        var nights = NightRange.Create(request.FirstNight, request.LastNight);
        var now = clock.UtcNow;
        var created = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            availability.EnsureUnavailabilityAllowed(nights, await session.ListOverlappingBookingsAsync(nights.AsStay), now);
            var unavailability = BoatUnavailability.Create(BoatUnavailabilityId.New(), boat.Id, nights, request.Comments, now);
            session.SaveUnavailability(unavailability);
            return unavailability;
        });
        return ToDto(created);
    }

    public async Task<UnavailabilityDto> UpdateAsync(string boatId, string id, SaveUnavailabilityRequest request, CancellationToken ct = default)
    {
        var boat = await BoatAsync(boatId, ct);
        var nights = NightRange.Create(request.FirstNight, request.LastNight);
        var now = clock.UtcNow;
        var updated = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var unavailability = await session.GetUnavailabilityAsync(new BoatUnavailabilityId(id));
            if (unavailability is null || unavailability.BoatId != boat.Id)
            {
                throw new NotFoundException("We couldn't find that unavailability.");
            }

            unavailability.EnsureVersion(request.Version);
            availability.EnsureUnavailabilityAllowed(nights, await session.ListOverlappingBookingsAsync(nights.AsStay), now);
            unavailability.Update(nights, request.Comments, now);
            session.SaveUnavailability(unavailability);
            return unavailability;
        });
        return ToDto(updated);
    }

    public async Task DeleteAsync(string boatId, string id, CancellationToken ct = default)
    {
        var boat = await BoatAsync(boatId, ct);
        await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var unavailability = await session.GetUnavailabilityAsync(new BoatUnavailabilityId(id));
            if (unavailability is null || unavailability.BoatId != boat.Id)
            {
                throw new NotFoundException("We couldn't find that unavailability.");
            }

            session.DeleteUnavailability(unavailability);
            return true;
        });
    }

    private static UnavailabilityDto ToDto(BoatUnavailability u) =>
        new(u.Id.Value, u.BoatId.Value, u.Nights.FirstNight, u.Nights.LastNight, u.Comments, u.Version);

    private async Task<Boat> BoatAsync(string boatId, CancellationToken ct) =>
        await boats.GetAsync(new BoatId(boatId), ct) ?? throw new NotFoundException("We couldn't find that boat.");
}
