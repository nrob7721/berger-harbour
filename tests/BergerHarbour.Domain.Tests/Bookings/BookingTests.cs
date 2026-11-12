using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Tests.Support;
using static BergerHarbour.Domain.Tests.Support.TestBookings;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class BookingTests
{
    [Fact]
    public void Payment_status_outstanding_then_part_paid_below_deposit_then_deposit_paid_then_fully_paid()
    {
        var b = Staff("2027-03-08", "2027-03-12", hire: 4060, deposit: 1000);
        Assert.Equal(PaymentStatus.Outstanding, b.PaymentStatus);
        b.RecordPayment(Paid(500), Now);
        Assert.Equal(PaymentStatus.PartPaid, b.PaymentStatus);
        b.RecordPayment(Paid(500), Now);
        Assert.Equal(PaymentStatus.DepositPaid, b.PaymentStatus);
        b.RecordPayment(Paid(1000), Now);
        Assert.Equal(PaymentStatus.PartPaid, b.PaymentStatus);
        b.RecordPayment(Paid(2060), Now);
        Assert.Equal(PaymentStatus.FullyPaid, b.PaymentStatus);
        Assert.Equal(0m, b.AmountOwing);
    }

    [Fact]
    public void Total_counts_every_priced_line_and_excludes_unpriced_ones()
    {
        var b = Staff("2027-03-08", "2027-03-12", hire: 4060, lines: [Line("motor", 1, 95), Line("ice", 3, 10), Line("hamper", 1)]);
        Assert.Equal(4060m + 95m + 30m, b.TotalPrice);
    }

    [Fact]
    public void Stripe_payments_are_idempotent_on_the_payment_intent()
    {
        var b = Staff("2027-03-08", "2027-03-12");
        Assert.True(b.RecordPayment(Paid(1000, "pi_1"), Now));
        Assert.False(b.RecordPayment(Paid(1000, "pi_1"), Now));
        Assert.Equal(1000m, b.AmountPaid);
    }

    [Fact]
    public void Staff_bookings_start_active_with_their_reference_and_activation_time()
    {
        var b = Staff("2027-01-15", "2027-01-19");
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal("SMITH-2701150", b.Reference!.Value);
        Assert.Equal(Now, b.ActivatedAt);
        Assert.Equal(BookingCreator.SystemUser, b.CreatedBy);
        Assert.Equal(new TimeOnly(11, 0), b.CheckInTime);
        Assert.Equal(new TimeOnly(8, 0), b.CheckOutTime);
    }

    [Fact]
    public void Online_hold_has_no_reference_or_customer_until_activated()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Equal(BookingStatus.PendingPayment, hold.Status);
        Assert.Null(hold.Reference);
        Assert.Null(hold.CustomerId);
        Assert.Throws<InvalidOperationException>(() => hold.ActivateFromDeposit(Now));
        hold.AssignReference(BookingReference.Create("SMITH-270308", 0));
        hold.ActivateFromDeposit(Now.AddMinutes(5));
        Assert.Equal(BookingStatus.Active, hold.Status);
        Assert.Equal(Now.AddMinutes(5), hold.ActivatedAt);
        Assert.Null(hold.HoldExpiresAt);
    }

    [Fact]
    public void Legal_transitions()
    {
        var b = Staff("2027-03-08", "2027-03-12");
        b.Cancel(Now);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        b.Reactivate(Now.AddDays(1));
        Assert.Equal(BookingStatus.Active, b.Status);
        Assert.Equal(Now, b.ActivatedAt); // not changed by Cancelled → Active

        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        hold.Expire(Now);
        hold.Expire(Now); // idempotent
        Assert.Equal(BookingStatus.Expired, hold.Status);
    }

    [Fact]
    public void Illegal_transitions()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        Assert.Throws<DomainValidationException>(() => hold.Cancel(Now));
        var active = Staff("2027-03-08", "2027-03-12");
        Assert.Throws<DomainValidationException>(() => active.Reactivate(Now));
        Assert.Throws<InvalidOperationException>(() => active.Expire(Now));
        Assert.Throws<InvalidOperationException>(() => active.Supersede(Now));
        Assert.Throws<DomainValidationException>(() => active.ChangeStatusByStaff(BookingStatus.Expired, Now));
    }

    [Fact]
    public void An_Expired_hold_activates_on_a_deposit_or_staff_confirmation()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        hold.Expire(Now);
        Assert.True(hold.CanBeConfirmedByStaff);
        hold.RecordStaffConfirmation(new Payment("p1", 1000, PaymentMethod.Manual, null, Now, null, BookingCreator.SystemUser), Now);
        hold.AssignReference(BookingReference.Create("SMITH-270308", 0));
        hold.ActivateFromDeposit(Now.AddDays(2));
        Assert.Equal(BookingStatus.Active, hold.Status);
        Assert.Equal(Now.AddDays(2), hold.ActivatedAt);
        Assert.Equal(Now.AddDays(2), hold.ActivationAnchor);
        Assert.Equal(BookingCreator.Customer, hold.CreatedBy);
    }

    [Fact]
    public void Staff_confirmation_needs_an_Expired_hold_without_payments_and_at_least_the_checkout_amount()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40), hire: 800);
        Assert.Equal(800m, hold.CheckoutAmount);
        Payment Manual(decimal amount) => new(Guid.NewGuid().ToString("N"), amount, PaymentMethod.Manual, null, Now, null, BookingCreator.SystemUser);
        Assert.Throws<BusinessRuleException>(() => hold.RecordStaffConfirmation(Manual(800), Now));
        hold.Expire(Now);
        var e = Assert.Throws<DomainValidationException>(() => hold.RecordStaffConfirmation(Manual(799.99m), Now));
        Assert.Equal("To confirm this booking, record at least $800.00.", e.Errors.ByField["amount"][0]);
        Assert.Empty(hold.Payments);
        hold.RecordPayment(new Payment("p", 800, PaymentMethod.Stripe, "pi", Now, null, BookingCreator.Customer), Now);
        Assert.False(hold.CanBeConfirmedByStaff);
        Assert.Throws<BusinessRuleException>(() => hold.RecordStaffConfirmation(Manual(800), Now));
    }

    [Fact]
    public void A_lapsed_hold_expires_and_staff_cannot_update_holds()
    {
        var hold = Hold("2027-03-08", "2027-03-12", Now.AddMinutes(40));
        hold.ExpireIfLapsed(Now.AddMinutes(39));
        Assert.Equal(BookingStatus.PendingPayment, hold.Status);
        Assert.Throws<BusinessRuleException>(() => hold.EnsureEditableByStaff());
        Assert.True(hold.IsLapsedHold(Now.AddMinutes(40)));
        hold.ExpireIfLapsed(Now.AddMinutes(40));
        Assert.Equal(BookingStatus.Expired, hold.Status);
        Assert.Throws<BusinessRuleException>(() => hold.EnsureEditableByStaff());
        Staff("2027-03-08", "2027-03-12").EnsureEditableByStaff();
    }

    [Fact]
    public void What_blocks_availability()
    {
        Assert.True(Staff("2027-03-08", "2027-03-12").BlocksAvailability(Now));
        Assert.False(Staff("2027-03-08", "2027-03-12", standby: true).BlocksAvailability(Now));
        Assert.True(Hold("2027-03-08", "2027-03-12", Now.AddMinutes(1)).BlocksAvailability(Now));
        Assert.False(Hold("2027-03-08", "2027-03-12", Now).BlocksAvailability(Now));
        var cancelled = Staff("2027-03-08", "2027-03-12");
        cancelled.Cancel(Now);
        Assert.False(cancelled.BlocksAvailability(Now));
    }

    [Fact]
    public void Guests_need_at_least_one_adult()
    {
        var e = Assert.Throws<DomainValidationException>(() => Booking.CreateByStaff(BookingId.New(), BookingReference.Create("A-270308", 0),
            new Domain.Customers.CustomerId("c"), new StaffBookingDetails(PacificBlue, PeriodType.Custom, StayRange.Create(D("2027-03-08"), D("2027-03-12")),
                0, -1, Sydney.StandardCheckIn, Sydney.StandardCheckOut, false, null, 1, PaymentScheduleType.Standard, []), 1000, "h", Now));
        Assert.True(e.Errors.Has("numberOfAdults"));
        Assert.True(e.Errors.Has("numberOfChildren"));
    }

    [Fact]
    public void The_reference_never_changes_when_dates_or_name_change()
    {
        var b = Staff("2027-01-15", "2027-01-19", lastName: "Smith");
        b.UpdateByStaff(new StaffBookingDetails(PacificBlue, PeriodType.Custom, StayRange.Create(D("2027-02-01"), D("2027-02-05")), 2, 0,
            Sydney.StandardCheckIn, Sydney.StandardCheckOut, false, null, 3000, PaymentScheduleType.Standard, []), Now);
        b.AssignReference(BookingReference.Create(BookingReference.PrefixFor("Jones", D("2027-02-01")), 0));
        Assert.Equal("SMITH-2701150", b.Reference!.Value);
    }
}
