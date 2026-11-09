using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.Bookings;

namespace BergerHarbour.Application.Bookings;

/// <summary>The Summary step's server quote (<c>BoatQuotesController</c>).</summary>
public sealed class OnlineQuoteService
{
    private readonly OnlineBookingPreparer preparer;
    private readonly PaymentScheduleService schedule;
    private readonly IClock clock;

    public OnlineQuoteService(OnlineBookingPreparer preparer, PaymentScheduleService schedule, IClock clock)
    {
        this.preparer = preparer;
        this.schedule = schedule;
        this.clock = clock;
    }

    /// <summary>Runs the online rules (except the draft, terms and acceptance) and prices the stay.</summary>
    public async Task<QuoteResponse> QuoteAsync(string slug, QuoteRequest request, CancellationToken ct = default)
    {
        var boat = await preparer.HouseBoatAsync(slug, ct);
        var p = await preparer.PrepareAsync(boat, OnlineRequestMapper.ToRequest(request), BookingValidationScope.Quote, null, ct);
        var milestones = schedule.Milestones(p.PaymentSchedule, p.Stay.Start, p.DepositAmount, p.TotalPrice, clock.UtcNow);
        var lines = p.AddonLines.Select(l => new QuoteAddonLine(l.AddonId.Value, l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal)).ToList();
        return new QuoteResponse(boat.Name, request.PeriodType, p.Stay.Start, p.Stay.End, request.NumberOfAdults, request.NumberOfChildren, p.Season.Name,
            p.HirePrice, lines, p.TotalPrice, lines.Any(l => l.UnitPrice is null), p.DepositAmount, p.CheckoutAmount, ScheduleItems.From(milestones),
            boat.SecurityBond, p.RoomingWarning);
    }
}
