using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

public class OnlineBookingServiceTests
{
    private readonly ApplicationHarness h = new();

    [Fact]
    public async Task Creating_a_booking_holds_the_dates_without_a_customer_or_reference_and_charges_min_of_deposit_and_hire()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        var hold = h.Booking(created.BookingId);
        Assert.Equal(BookingStatus.PendingPayment, hold.Status);
        Assert.Null(hold.Reference);
        Assert.Null(hold.CustomerId);
        Assert.Empty(h.Db.Customers.All);
        Assert.Equal("cs_test_1_secret", created.ClientSecret);
        Assert.Equal(1000m, h.Payments.Created.Single().Amount);
        Assert.Equal(CheckoutPurpose.Deposit, h.Payments.Created.Single().Purpose);
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromMinutes(40), hold.HoldExpiresAt);
        Assert.Equal(DraftStep.Payment, h.Db.Drafts.Get(new BookingDraftId(draft))!.Contents.LastStepReached);
    }

    [Fact]
    public async Task Prices_are_calculated_on_the_server()
    {
        h.PriceAddon("ice", 10, 10, 15);
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft, addons: [new AddonRequestDto("ice", 3), new AddonRequestDto("welcome-hamper", 1)]));
        var hold = h.Booking(created.BookingId);
        Assert.Equal(4060m, hold.HirePrice);
        Assert.Equal(4060m + 30m, hold.TotalPrice);
        Assert.Null(hold.AddonLines.Single(l => l.AddonId.Value == "welcome-hamper").UnitPrice);
    }

    [Fact]
    public async Task A_second_hold_on_the_same_dates_is_409()
    {
        await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        var e = await Assert.ThrowsAsync<ConflictException>(async () =>
            await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync(email: "other@example.com"), email: "other@example.com")));
        Assert.Equal(ConflictException.DatesTaken, e.Message);
    }

    [Fact]
    public async Task Online_addon_capacity_is_a_hard_limit()
    {
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", slug: "pacific-blue",
            lines: [new StaffAddonLineInput(null, "car-parking", 10, null)]));
        h.Activate("misty-blue");
        var draft = await h.CreateDraftAsync(slug: "misty-blue");
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Online.CreateAsync(BookingRequest(draft, slug: "misty-blue", addons: [new AddonRequestDto("car-parking", 2)])));
        Assert.Equal(["Only 1 car spots remain for your dates."], e.Errors.ByField["addons"]);
    }

    [Fact]
    public async Task Release_expires_the_session_and_the_hold_freeing_the_dates_and_is_idempotent()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        await h.Online.ReleaseAsync(created.BookingId, new ReleaseBookingRequest(draft));
        Assert.Equal(BookingStatus.Expired, h.Booking(created.BookingId).Status);
        Assert.Contains(created.SessionId, h.Payments.Expired);
        await h.Online.ReleaseAsync(created.BookingId, new ReleaseBookingRequest(draft));
        var again = await h.Online.CreateAsync(BookingRequest(draft));
        Assert.NotEqual(created.BookingId, again.BookingId);
        Assert.Empty(h.References.Counters);
    }

    [Fact]
    public async Task Release_with_a_wrong_draft_is_404_and_after_payment_is_409()
    {
        var draft = await h.CreateDraftAsync();
        var created = await h.Online.CreateAsync(BookingRequest(draft));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Online.ReleaseAsync(created.BookingId, new ReleaseBookingRequest("other")));
        h.Payments.Completed.Add(created.SessionId);
        await Assert.ThrowsAsync<ConflictException>(() => h.Online.ReleaseAsync(created.BookingId, new ReleaseBookingRequest(draft)));
        await h.PayDepositAsync(created.BookingId);
        await Assert.ThrowsAsync<ConflictException>(() => h.Online.ReleaseAsync(created.BookingId, new ReleaseBookingRequest(draft)));
    }

    [Fact]
    public async Task Status_requires_the_matching_session_and_shows_the_reference_once_assigned()
    {
        var draft = await h.CreateDraftAsync(start: "2027-01-11");
        var created = await h.Online.CreateAsync(BookingRequest(draft, start: "2027-01-11"));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Online.GetStatusAsync(created.BookingId, "cs_other"));
        Assert.Null((await h.Online.GetStatusAsync(created.BookingId, created.SessionId)).Reference);
        await h.PayDepositAsync(created.BookingId);
        var status = await h.Online.GetStatusAsync(created.BookingId, created.SessionId);
        Assert.Equal(BookingStatus.Active, status.Status);
        Assert.Equal("SMITH-2701110", status.Reference);
        Assert.Equal("jane@example.com", status.Email);
    }

    [Fact]
    public async Task Updating_a_draft_with_another_boat_changes_its_boat_and_the_booking_is_accepted_for_it()
    {
        h.Activate("misty-blue");
        var draft = await h.CreateDraftAsync();
        await h.Get<BookingDraftService>().UpdateAsync(draft, new UpdateDraftRequest("misty-blue", PeriodType.Midweek, D("2027-03-08"), 2, 0, "Jane", "Smith",
            "jane@example.com", "+61412345678", [], DraftStep.Summary));
        Assert.Equal(h.Boat("misty-blue").Id, h.Db.Drafts.Get(new BookingDraftId(draft))!.Contents.BoatId);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Online.CreateAsync(BookingRequest(draft)));
        var created = await h.Online.CreateAsync(BookingRequest(draft, slug: "misty-blue"));
        Assert.Equal(h.Boat("misty-blue").Id, h.Booking(created.BookingId).BoatId);
    }

    [Fact]
    public async Task Quote_returns_prices_schedule_and_rooming_warning_and_runs_the_rules()
    {
        var quote = await h.Get<OnlineQuoteService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Midweek, D("2027-03-08"), 9, 0, []));
        Assert.Equal("Normal", quote.SeasonName);
        Assert.Equal(4060m, quote.HirePrice);
        Assert.Equal(1000m, quote.AmountDueAtCheckout);
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.Balance], quote.PaymentSchedule.Select(s => s.Kind));
        Assert.Equal(3060m, quote.PaymentSchedule[1].Amount);
        Assert.NotNull(quote.RoomingWarning);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Get<OnlineQuoteService>().QuoteAsync("pacific-blue", new QuoteRequest(PeriodType.Midweek, D("2026-11-16"), 2, 0, [])));
    }
}
