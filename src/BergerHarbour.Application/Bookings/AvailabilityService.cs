using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Bookings;

/// <summary>Fleet and period availability for the booking app (<c>AvailabilityController</c>). Ranges only, never booking data.</summary>
public sealed class AvailabilityService
{
    private const int MaxSpanMonths = 15;

    private readonly IBoatRepository boats;
    private readonly IBookingRepository bookings;
    private readonly IBoatUnavailabilityRepository unavailabilities;
    private readonly ReferenceDataLoader referenceData;
    private readonly BookingAvailabilityService availability;
    private readonly OnlineStayPolicy stayPolicy;
    private readonly PricingService pricing;
    private readonly IClock clock;

    public AvailabilityService(
        IBoatRepository boats, IBookingRepository bookings, IBoatUnavailabilityRepository unavailabilities, ReferenceDataLoader referenceData,
        BookingAvailabilityService availability, OnlineStayPolicy stayPolicy, PricingService pricing, IClock clock)
    {
        this.boats = boats;
        this.bookings = bookings;
        this.unavailabilities = unavailabilities;
        this.referenceData = referenceData;
        this.availability = availability;
        this.stayPolicy = stayPolicy;
        this.pricing = pricing;
        this.clock = clock;
    }

    /// <summary>Every active house boat's unavailable ranges in <c>[from, to)</c>, and the blocked periods. Max 15 months.</summary>
    public async Task<FleetAvailabilityResponse> GetFleetAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (from is not { } start || to is not { } end)
        {
            throw new DomainValidationException(from is null ? "from" : "to", "Enter both dates.");
        }

        var range = StayRange.Create(start, end, "to");
        if (end > start.AddMonths(MaxSpanMonths))
        {
            throw new DomainValidationException("to", $"Ask for at most {MaxSpanMonths} months at a time.");
        }

        var now = clock.UtcNow;
        var fleet = await ActiveHouseBoatsAsync(ct);
        var reference = await referenceData.LoadAsync(ct);
        var blocking = (await bookings.ListOverlappingAsync(start, end, ct)).Where(b => b.BlocksAvailability(now)).ToList();
        var unavailable = await unavailabilities.ListInRangeAsync(start, end, ct);
        var boatRanges = fleet.Select(boat => new BoatAvailability(
                boat.Slug,
                blocking.Where(b => b.BoatId == boat.Id).Select(b => b.Stay)
                    .Concat(unavailable.Where(u => u.BoatId == boat.Id).Select(u => u.Nights.AsStay))
                    .OrderBy(s => s.Start)
                    .Select(s => new StayRangeDto(s.Start, s.End))
                    .ToList()))
            .ToList();
        var enquireOnly = reference.BlockedPeriods.Where(p => p.Nights.Overlaps(range)).OrderBy(p => p.Nights.FirstNight)
            .Select(p => new EnquireOnlyRange(p.Name, p.Nights.FirstNight, p.Nights.LastNight))
            .ToList();
        return new FleetAvailabilityResponse(enquireOnly, boatRanges);
    }

    /// <summary>For one online stay: each active house boat's availability and hire price. 400 if the stay breaks §7.7 rules 2–5.</summary>
    public async Task<PeriodAvailabilityResponse> GetPeriodAsync(PeriodType? periodType, DateOnly? startDate, CancellationToken ct = default)
    {
        if (periodType is not { } type || startDate is not { } start)
        {
            throw new DomainValidationException(periodType is null ? "periodType" : "startDate", "Choose a period.");
        }

        var now = clock.UtcNow;
        var reference = await referenceData.LoadAsync(ct);
        var check = stayPolicy.Check(type, start, reference.Settings, reference.BlockedPeriods, Sydney.DateOf(now));
        check.Errors.ThrowIfAny();
        var stay = check.Stay!.Value;
        var fleet = await ActiveHouseBoatsAsync(ct);
        var overlapping = await bookings.ListOverlappingAsync(stay.Start, stay.End, ct);
        var unavailable = await unavailabilities.ListInRangeAsync(stay.Start, stay.End, ct);
        var result = fleet.Select(boat => new PeriodBoatAvailability(
                boat.Slug,
                availability.IsFree(stay, overlapping.Where(b => b.BoatId == boat.Id), unavailable.Where(u => u.BoatId == boat.Id), now),
                pricing.HirePrice(boat, type, stay.Start, reference.Seasons)))
            .ToList();
        return new PeriodAvailabilityResponse(type, stay.Start, stay.End, result);
    }

    private async Task<IReadOnlyList<Boat>> ActiveHouseBoatsAsync(CancellationToken ct) =>
        Boat.InListOrder((await boats.ListAsync(ct)).Where(b => b.IsActive && b.Type == BoatType.House)).ToList();
}
