using System.Text.Json;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

public class AvailabilityServiceTests
{
    private readonly ApplicationHarness h = new();

    private AvailabilityService Service => h.Get<AvailabilityService>();

    [Fact]
    public async Task Fleet_availability_lists_only_active_house_boats_with_ranges_and_no_booking_data()
    {
        h.Activate("misty-blue");
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12"));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-15", "2027-03-19", standby: true));
        h.AddUnavailability("misty-blue", "2027-03-20", "2027-03-22");
        var result = await Service.GetFleetAsync(D("2027-03-01"), D("2027-04-01"));
        Assert.Equal(["pacific-blue", "misty-blue"], result.Boats.Select(b => b.Slug));
        var pacific = result.Boats[0].Unavailable.Single();
        Assert.Equal((D("2027-03-08"), D("2027-03-12")), (pacific.Start, pacific.End));
        var misty = result.Boats[1].Unavailable.Single();
        Assert.Equal((D("2027-03-20"), D("2027-03-23")), (misty.Start, misty.End));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("john", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMITH", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fleet_availability_lists_blocked_periods_as_enquire_only()
    {
        var result = await Service.GetFleetAsync(D("2026-12-01"), D("2027-01-31"));
        Assert.Equal("Christmas / New Year 2026–27", result.EnquireOnly.Single().Name);
    }

    [Fact]
    public async Task Fleet_span_is_at_most_15_months() =>
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GetFleetAsync(D("2027-01-01"), D("2028-04-02")));

    [Fact]
    public async Task Period_availability_reports_each_boat_and_its_price_from_the_pricing_service()
    {
        h.Activate("misty-blue");
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-09", "2027-03-11", slug: "misty-blue"));
        var result = await Service.GetPeriodAsync(PeriodType.Midweek, D("2027-03-08"));
        Assert.Equal(D("2027-03-12"), result.EndDate);
        var pacific = result.Boats.Single(b => b.Slug == "pacific-blue");
        Assert.True(pacific.Available);
        Assert.Equal(4060m, pacific.HirePrice);
        var misty = result.Boats.Single(b => b.Slug == "misty-blue");
        Assert.False(misty.Available);
        Assert.Equal(1500m, misty.HirePrice);
    }

    [Fact]
    public async Task Standbys_cancelled_expired_superseded_and_expired_holds_dont_make_a_boat_unavailable()
    {
        var standby = await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", standby: true));
        var cancelled = await h.Staff.CreateAsync(h.StaffRequest("2027-03-15", "2027-03-19"));
        await h.Staff.UpdateAsync(cancelled.Booking.Id, UpdateRequest(cancelled.Booking, r => r with { Status = BookingStatus.Cancelled }));
        var draft = await h.CreateDraftAsync(start: "2027-03-22");
        await h.Online.CreateAsync(BookingRequest(draft, start: "2027-03-22"));
        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await Service.GetPeriodAsync(PeriodType.Midweek, D("2027-03-08"))).Boats.Single().Available);
        Assert.True((await Service.GetPeriodAsync(PeriodType.Midweek, D("2027-03-15"))).Boats.Single().Available);
        Assert.True((await Service.GetPeriodAsync(PeriodType.Midweek, D("2027-03-22"))).Boats.Single().Available);
        Assert.NotNull(standby);
    }

    [Theory]
    [InlineData(PeriodType.Midweek, "2027-03-09")]
    [InlineData(PeriodType.Midweek, "2026-11-30")]
    [InlineData(PeriodType.Midweek, "2027-11-29")]
    [InlineData(PeriodType.Weekend, "2026-12-18")]
    public async Task Invalid_stays_are_400(PeriodType type, string start) =>
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GetPeriodAsync(type, D(start)));
}
