using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

/// <summary>Payments edited as part of the booking (decision 38, §8.2).</summary>
public class StaffBookingPaymentsTests
{
    private readonly ApplicationHarness h = new();

    private int PaymentReceived => h.CountEmails("Payment received");

    [Fact]
    public async Task Payments_saved_with_a_new_booking_are_recorded_dated_today_and_send_no_5()
    {
        var saved = await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19") with { Payments = [NewPayment(500, "2026-11-09", "Cash")] });
        var payment = Assert.Single(saved.Booking.Payments);
        Assert.Equal((500m, PaymentMethod.Manual, "Cash", h.Clock.UtcNow), (payment.Amount, payment.Method, payment.Note, payment.PaidAt));
        Assert.Equal(0, PaymentReceived);

        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.CreateAsync(h.StaffRequest("2027-02-15", "2027-02-19") with { Payments = [NewPayment(500, "2026-11-08")] }));
        Assert.Equal("The payment date can't be before the booking was created (09/11/2026).", e.Errors.ByField["payments"][0]);
        Assert.Single(h.Db.Bookings.All);
    }

    [Fact]
    public async Task New_payments_are_added_kept_payments_unchanged_and_left_out_payments_removed()
    {
        var saved = (await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19") with
        {
            Payments = [NewPayment(500, "2026-11-09", "First"), NewPayment(300, "2026-11-09", "Second")],
        })).Booking;
        h.Clock.Advance(TimeSpan.FromDays(2));
        var first = saved.Payments.Single(p => p.Note == "First");

        var updated = (await h.Staff.UpdateAsync(saved.Id, UpdateRequest(saved, r => r with
        {
            Payments = [new StaffPaymentInput(first.Id, 999, D("2026-11-11"), "ignored"), NewPayment(250, "2026-11-10", "Third")],
        }))).Booking;

        Assert.Equal(["First", "Third"], updated.Payments.Select(p => p.Note));
        Assert.Equal((first.Amount, first.PaidAt), (updated.Payments[0].Amount, updated.Payments[0].PaidAt));
        Assert.Equal(Sydney.InstantOf(D("2026-11-10"), new TimeOnly(12, 0)), updated.Payments[1].PaidAt);
        Assert.Equal(750m, updated.AmountPaid);
        Assert.Equal(BookingStatus.Active, updated.Status);
        Assert.Equal(1, PaymentReceived);
    }

    [Fact]
    public async Task Each_payment_added_to_an_Active_booking_sends_one_5_and_removing_one_sends_nothing()
    {
        var saved = (await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"))).Booking;
        var paid = (await h.Staff.UpdateAsync(saved.Id, UpdateRequest(saved, r => r with
        {
            Payments = [NewPayment(100, "2026-11-09"), NewPayment(200, "2026-11-09")],
        }))).Booking;
        Assert.Equal(2, PaymentReceived);

        var removed = (await h.Staff.UpdateAsync(saved.Id, UpdateRequest(paid, r => r with { Payments = [] }))).Booking;
        Assert.Empty(removed.Payments);
        Assert.Equal(PaymentStatus.Outstanding, removed.PaymentStatus);
        Assert.Equal(2, PaymentReceived);
    }

    [Fact]
    public async Task Payments_are_accepted_on_Cancelled_and_Superseded_bookings_without_a_5()
    {
        var created = (await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"))).Booking;
        h.Emails.Sent.Clear();
        var cancelled = (await h.Staff.UpdateAsync(created.Id, UpdateRequest(created, r => r with
        {
            Status = BookingStatus.Cancelled, Payments = [NewPayment(100, "2026-11-09")],
        }))).Booking;
        Assert.Equal((BookingStatus.Cancelled, 100m), (cancelled.Status, cancelled.AmountPaid));

        var standby = (await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", standby: true, email: "credit@example.com"))).Booking;
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        var superseded = await h.Staff.GetAsync(standby.Id);
        var paid = (await h.Staff.UpdateAsync(standby.Id, UpdateRequest(superseded, r => r with { Payments = [NewPayment(50, "2026-11-09")] }))).Booking;
        Assert.Equal((BookingStatus.Superseded, 50m), (paid.Status, paid.AmountPaid));
        Assert.Equal(0, PaymentReceived);
    }

    [Fact]
    public async Task A_payment_dated_in_the_future_is_rejected_on_update_and_nothing_is_saved()
    {
        var saved = (await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"))).Booking;
        var e = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Staff.UpdateAsync(saved.Id, UpdateRequest(saved, r => r with { Payments = [NewPayment(100, "2026-11-10")] })));
        Assert.Equal("The payment date can't be in the future.", e.Errors.ByField["payments"][0]);
        Assert.Empty(h.Booking(saved.Id).Payments);
    }

    [Fact]
    public async Task Payment_changes_are_rejected_for_holds()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        var hold = await h.Staff.GetAsync(created.BookingId);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            h.Staff.UpdateAsync(hold.Id, UpdateRequest(hold, r => r with { Payments = [NewPayment(100, "2026-11-09")] })));
        Assert.Empty(h.Booking(created.BookingId).Payments);
    }

    [Fact]
    public async Task A_webhook_replay_for_a_stripe_payment_staff_removed_records_nothing()
    {
        var booking = await h.BookOnlineAsync();
        var detail = await h.Staff.GetAsync(booking.Id.Value);
        Assert.Single(detail.Payments);
        await h.Staff.UpdateAsync(detail.Id, UpdateRequest(detail, r => r with { Payments = [] }));
        var sent = h.Emails.Sent.Count;

        await h.PayDepositAsync(booking.Id.Value);

        var stored = h.Booking(booking.Id.Value);
        Assert.Empty(stored.Payments);
        Assert.Single(stored.RemovedStripePaymentIntentIds);
        Assert.Equal(BookingStatus.Active, stored.Status);
        Assert.Equal(sent, h.Emails.Sent.Count);
    }

    [Fact]
    public async Task Payments_on_an_Expired_booking_follow_the_date_rule()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromDays(1));
        var expired = await h.Staff.GetAsync(created.BookingId);
        var before = await Assert.ThrowsAsync<DomainValidationException>(() => h.SaveWithPaymentAsync(expired, 1000, "2026-11-08"));
        Assert.True(before.Errors.Has("payments"));
        var future = await Assert.ThrowsAsync<DomainValidationException>(() => h.SaveWithPaymentAsync(expired, 1000, "2026-11-11"));
        Assert.True(future.Errors.Has("payments"));
        Assert.Empty(h.Booking(created.BookingId).Payments);
        Assert.Equal(BookingStatus.Active, (await h.SaveWithPaymentAsync(expired, 1000, "2026-11-09")).Status);
    }

    [Fact]
    public async Task Payment_changes_are_rejected_on_a_PendingPayment_hold()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        var pending = await h.Staff.GetAsync(created.BookingId);
        await Assert.ThrowsAsync<BusinessRuleException>(() => h.SaveWithPaymentAsync(pending, 1000, "2026-11-09"));
        Assert.Empty(h.Booking(created.BookingId).Payments);
    }
}
