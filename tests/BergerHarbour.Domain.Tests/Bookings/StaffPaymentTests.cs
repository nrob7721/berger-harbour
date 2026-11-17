using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class StaffPaymentTests
{
    // Created 00:30 on 09/11/2026 in Sydney (AEDT), which is still 08/11 in UTC.
    private static readonly DateTimeOffset Created = At("2026-11-08T13:30:00Z");
    private static readonly DateTimeOffset Later = At("2026-11-12T01:00:00Z"); // 12/11/2026 in Sydney

    private static Payment Manual(decimal amount, string date, DateTimeOffset now) => Payment.Manual(amount, D(date), null, now);

    [Fact]
    public void The_date_can_be_the_booking_creation_date_or_today_as_Sydney_dates()
    {
        var b = Staff("2027-03-08", "2027-03-12", now: Created);
        b.ChangePaymentsByStaff([], [Manual(100, "2026-11-09", Later), Manual(200, "2026-11-12", Later)], Later);
        Assert.Equal(300m, b.AmountPaid);
    }

    [Fact]
    public void The_date_cannot_be_before_the_booking_was_created_or_in_the_future()
    {
        var b = Staff("2027-03-08", "2027-03-12", now: Created);
        var before = Assert.Throws<DomainValidationException>(() => b.ChangePaymentsByStaff([], [Manual(100, "2026-11-08", Later)], Later));
        Assert.Equal("The payment date can't be before the booking was created (09/11/2026).", before.Errors.ByField["date"][0]);
        var future = Assert.Throws<DomainValidationException>(() => b.ChangePaymentsByStaff([], [Manual(100, "2026-11-13", Later)], Later));
        Assert.Equal("The payment date can't be in the future.", future.Errors.ByField["date"][0]);
        Assert.Empty(b.Payments);
    }

    [Fact]
    public void A_payment_dated_today_is_paid_now_and_an_earlier_one_at_noon_Sydney()
    {
        Assert.Equal(Later, Manual(100, "2026-11-12", Later).PaidAt);
        Assert.Equal(At("2026-11-10T01:00:00Z"), Manual(100, "2026-11-10", Later).PaidAt);
    }

    [Fact]
    public void Amount_and_note_are_validated()
    {
        var e = Assert.Throws<DomainValidationException>(() => Payment.Manual(0, D("2026-11-09"), new string('x', 501), Now));
        Assert.True(e.Errors.Has("amount"));
        Assert.True(e.Errors.Has("note"));
        Assert.Equal("Cash", Payment.Manual(10, D("2026-11-09"), "  Cash ", Now).Note);
    }

    [Fact]
    public void Removing_payments_never_changes_the_status_and_remembers_removed_stripe_payments()
    {
        var b = Staff("2027-03-08", "2027-03-12");
        b.RecordPayment(Paid(1000, "pi_1"), Now);
        var manual = Paid(500);
        b.RecordPayment(manual, Now);
        b.ChangePaymentsByStaff([manual.Id], [], Now);
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal([manual.Id], b.Payments.Select(p => p.Id));
        Assert.Equal(["pi_1"], b.RemovedStripePaymentIntentIds);
        b.ChangePaymentsByStaff([], [], Now);
        Assert.Empty(b.Payments);
        Assert.Equal(["pi_1"], b.RemovedStripePaymentIntentIds);
        Assert.Equal(PaymentStatus.Outstanding, b.PaymentStatus);
    }

    [Fact]
    public void A_removed_stripe_payment_is_never_recorded_again()
    {
        var b = Staff("2027-03-08", "2027-03-12");
        b.RecordPayment(Paid(1000, "pi_1"), Now);
        b.ChangePaymentsByStaff([], [], Now);
        Assert.False(b.RecordPayment(Paid(1000, "pi_1"), Now));
        Assert.Empty(b.Payments);
    }

    [Fact]
    public void Payments_change_on_Active_Cancelled_and_Superseded_bookings_but_not_on_holds()
    {
        var cancelled = Staff("2027-03-08", "2027-03-12");
        cancelled.Cancel(Now);
        cancelled.ChangePaymentsByStaff([], [Manual(100, "2026-11-09", Now)], Now);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);

        var superseded = Staff("2027-03-08", "2027-03-12", standby: true);
        superseded.Supersede(Now);
        superseded.ChangePaymentsByStaff([], [Manual(100, "2026-11-09", Now)], Now);
        Assert.Equal(BookingStatus.Superseded, superseded.Status);

        var pending = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Throws<BusinessRuleException>(() => pending.ChangePaymentsByStaff([], [Manual(100, "2026-11-09", Now)], Now));
        pending.Expire(Now);
        Assert.Throws<BusinessRuleException>(() => pending.ChangePaymentsByStaff([], [Manual(100, "2026-11-09", Now)], Now));
        Assert.Empty(pending.Payments);
    }

    [Fact]
    public void Kept_ids_must_be_payments_of_the_booking()
    {
        var b = Staff("2027-03-08", "2027-03-12");
        var e = Assert.Throws<DomainValidationException>(() => b.ChangePaymentsByStaff(["nope"], [Manual(100, "2026-11-09", Now)], Now));
        Assert.True(e.Errors.Has("payments"));
    }

    [Fact]
    public void Staff_confirmation_follows_the_date_rule()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        hold.Expire(Now);
        var e = Assert.Throws<DomainValidationException>(() => hold.RecordStaffConfirmation(Manual(1000, "2026-11-08", Now), Now));
        Assert.True(e.Errors.Has("date"));
        Assert.Empty(hold.Payments);
    }
}
