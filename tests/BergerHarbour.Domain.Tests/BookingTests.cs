using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class BookingTests
{
    private static readonly BoatId Boat = BoatId.New();

    [Theory]
    [InlineData(0, PaymentStatus.Outstanding)]
    [InlineData(500, PaymentStatus.DepositPaid)]
    [InlineData(1000, PaymentStatus.DepositPaid)]
    [InlineData(1500, PaymentStatus.PartPaid)]
    [InlineData(4060, PaymentStatus.FullyPaid)]
    [InlineData(5000, PaymentStatus.FullyPaid)]
    public void Payment_status_is_derived_from_payments(int paid, PaymentStatus expected)
    {
        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        if (paid > 0)
        {
            booking.RecordManualPayment(paid, Now, null, Now);
        }

        Assert.Equal(expected, booking.PaymentStatus);
        Assert.Equal(paid, booking.AmountPaid);
        Assert.Equal(Math.Max(0, 4060 - paid), booking.AmountOwing);
    }

    [Fact]
    public void Stripe_payments_are_idempotent_on_payment_intent()
    {
        var booking = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        Assert.NotNull(booking.RecordStripePayment(1000m, "pi_1", Now, Now));
        Assert.Null(booking.RecordStripePayment(1000m, "pi_1", Now, Now));
        Assert.Equal(1000m, booking.AmountPaid);
    }

    [Fact]
    public void Online_hold_lifecycle()
    {
        var booking = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(BookingCreatedBy.Customer, booking.CreatedBy);
        booking.AttachCheckoutSession("cs_1", Now);
        booking.Activate(Now);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Null(booking.HoldExpiresAt);
    }

    [Fact]
    public void Hold_can_expire()
    {
        var booking = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        booking.Expire(Now);
        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.Throws<InvalidStateTransitionException>(() => booking.Activate(Now));
    }

    [Fact]
    public void Staff_bookings_start_active()
    {
        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Equal(BookingCreatedBy.SystemUser, booking.CreatedBy);
        Assert.Null(booking.HoldExpiresAt);
    }

    [Fact]
    public void Cancel_and_reinstate()
    {
        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        booking.Cancel(Now);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.False(booking.BlocksAvailability(Now));
        booking.Reinstate(Now);
        Assert.Equal(BookingStatus.Active, booking.Status);
    }

    [Fact]
    public void Illegal_transitions_are_rejected()
    {
        var active = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        Assert.Throws<InvalidStateTransitionException>(() => active.Activate(Now));
        Assert.Throws<InvalidStateTransitionException>(() => active.Expire(Now));
        Assert.Throws<InvalidStateTransitionException>(() => active.Reinstate(Now));
        Assert.Throws<InvalidStateTransitionException>(() => active.Supersede(Now)); // not a stand-by

        var hold = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        Assert.Throws<InvalidStateTransitionException>(() => hold.Cancel(Now));

        var standby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        standby.Supersede(Now);
        Assert.Throws<InvalidStateTransitionException>(() => standby.Supersede(Now));
        Assert.Throws<InvalidStateTransitionException>(() => standby.Cancel(Now));
        Assert.Throws<InvalidStateTransitionException>(() =>
            standby.ChangeDetails(Boat, PeriodType.Custom, new Stay(D(2026, 12, 7), D(2026, 12, 9)), 2, true, null, 1m,
                PaymentSchedule.Standard, Now));
    }

    [Fact]
    public void Hold_must_expire_in_the_future() =>
        Assert.Throws<DomainValidationException>(() => OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now));

    [Fact]
    public void Guests_must_be_at_least_one() =>
        Assert.Throws<DomainValidationException>(() =>
            Booking.CreateByStaff(NextReference(), CustomerId.New(), Boat, PeriodType.Custom,
                new Stay(D(2026, 12, 7), D(2026, 12, 11)), 0, false, null, 1m, 1000m, PaymentSchedule.Standard, "h", Now));

    [Fact]
    public void Active_non_standby_supersedes_overlapping_active_standbys_only()
    {
        var overlappingStandby = StaffBooking(Boat, D(2026, 12, 9), D(2026, 12, 12), standby: true);
        var touchingStandby = StaffBooking(Boat, D(2026, 12, 11), D(2026, 12, 14), standby: true);
        var otherBoatStandby = StaffBooking(BoatId.New(), D(2026, 12, 7), D(2026, 12, 11), standby: true);
        var cancelledStandby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        cancelledStandby.Cancel(Now);

        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        var superseded = BookingAvailabilityService.StandbysSupersededBy(booking,
            [overlappingStandby, touchingStandby, otherBoatStandby, cancelledStandby, booking]);

        Assert.Equal([overlappingStandby.Id], superseded.Select(b => b.Id));
    }

    [Fact]
    public void Pending_and_standby_bookings_supersede_nothing()
    {
        var standby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        var hold = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        var otherStandby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        Assert.Empty(BookingAvailabilityService.StandbysSupersededBy(hold, [standby]));
        Assert.Empty(BookingAvailabilityService.StandbysSupersededBy(otherStandby, [standby]));
    }

    [Fact]
    public void Standbys_may_overlap_each_other()
    {
        var standby = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true);
        BookingAvailabilityService.EnsureAvailable(Boat, new Stay(D(2026, 12, 7), D(2026, 12, 11)), [standby], [], Now);
    }

    [Fact]
    public void Customer_emails_only_for_active_non_standby_bookings()
    {
        Assert.True(StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11)).ReceivesCustomerEmails);
        Assert.False(StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11), standby: true).ReceivesCustomerEmails);
        Assert.False(OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddHours(1)).ReceivesCustomerEmails);
    }

    [Fact]
    public void Notifications_are_recorded_once()
    {
        var booking = StaffBooking(Boat, D(2026, 12, 7), D(2026, 12, 11));
        booking.RecordNotificationSent("PreHire", Now);
        booking.RecordNotificationSent("PreHire", Now.AddHours(1));
        Assert.True(booking.HasSentNotification("PreHire"));
        Assert.Single(booking.SentNotifications);
    }

    [Fact]
    public void Snapshot_round_trip()
    {
        var booking = OnlineHold(Boat, D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        booking.AddAddon(new RequestedAddon(AddonId.New(), "Ice", 3, null), Now);
        booking.RecordStripePayment(1000m, "pi_1", Now, Now);
        booking.RecordNotificationSent("x", Now);

        var copy = Booking.FromSnapshot(booking.ToSnapshot());

        Assert.Equivalent(booking.ToSnapshot(), copy.ToSnapshot());
    }

    [Theory]
    [InlineData(2026, 143, "BH-260143")]
    [InlineData(2027, 1, "BH-270001")]
    [InlineData(2026, 12345, "BH-2612345")]
    public void Reference_format(int year, int sequence, string expected) =>
        Assert.Equal(expected, BookingReference.Create(year, sequence).Value);

    [Fact]
    public void Reference_parsing_is_case_insensitive()
    {
        Assert.True(BookingReference.TryParse(" bh-260143 ", out var reference));
        Assert.Equal("BH-260143", reference!.Value);
        Assert.False(BookingReference.TryParse("hello", out _));
    }
}

public class LateDepositTests
{
    [Fact]
    public void Expired_online_booking_with_a_recorded_deposit_can_be_activated()
    {
        var booking = OnlineHold(BoatId.New(), D(2026, 12, 7), D(2026, 12, 11), Now.AddMinutes(40));
        booking.Expire(Now);
        Assert.Throws<InvalidStateTransitionException>(() => booking.Activate(Now));
        booking.RecordStripePayment(1000m, "pi_late", Now, Now);
        booking.Activate(Now);
        Assert.Equal(BookingStatus.Active, booking.Status);
    }
}
