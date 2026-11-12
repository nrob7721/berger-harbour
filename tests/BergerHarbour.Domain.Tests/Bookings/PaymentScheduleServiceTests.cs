using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Tests.Support;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Bookings;

public class PaymentScheduleServiceTests
{
    private readonly PaymentScheduleService schedule = new(PaymentScheduleOptions.Default);

    [Fact]
    public void Standard_schedule_is_the_deposit_then_100_percent_30_days_before_11am_check_in()
    {
        var m = schedule.Milestones(PaymentScheduleType.Standard, D("2027-03-08"), 1000, 4060, At("2026-11-09T00:00:00Z"));
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.Balance], m.Select(x => x.Kind));
        Assert.Equal(1000m, m[0].RequiredCumulative);
        Assert.Equal(4060m, m[1].RequiredCumulative);
        Assert.Equal(Sydney.InstantOf(D("2027-02-06"), new TimeOnly(11, 0)), m[1].DueAt);
        Assert.Equal(D("2027-02-06"), m[1].DueDate);
        Assert.False(m[1].DueAtActivation);
    }

    [Fact]
    public void Extended_schedule_is_50_percent_at_120_days_and_100_percent_at_90_days()
    {
        var m = schedule.Milestones(PaymentScheduleType.Extended, D("2026-12-21"), 1000, 8601, At("2026-01-09T00:00:00Z"));
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.First, MilestoneKind.Final], m.Select(x => x.Kind));
        Assert.Equal(4300.50m, m[1].RequiredCumulative);
        Assert.Equal(D("2026-08-23"), m[1].DueDate);
        Assert.Equal(8601m, m[2].RequiredCumulative);
        Assert.Equal(D("2026-09-22"), m[2].DueDate);
    }

    [Fact]
    public void Extended_schedule_applies_when_the_stay_overlaps_a_flagged_blocked_period()
    {
        var christmas = BlockedPeriod.Create(BlockedPeriodId.New(), "Christmas", NightRange.Create(D("2026-12-20"), D("2027-01-05")), true);
        var easter = BlockedPeriod.Create(BlockedPeriodId.New(), "Easter", NightRange.Create(D("2027-03-26"), D("2027-03-28")), false);
        Assert.Equal(PaymentScheduleType.Extended, schedule.ScheduleFor(StayRange.Create(D("2026-12-18"), D("2026-12-21")), [christmas, easter]));
        Assert.Equal(PaymentScheduleType.Standard, schedule.ScheduleFor(StayRange.Create(D("2027-03-26"), D("2027-03-29")), [christmas, easter]));
    }

    [Fact]
    public void A_milestone_due_before_creation_is_due_at_creation()
    {
        var m = schedule.Milestones(PaymentScheduleType.Standard, D("2026-11-30"), 1000, 4060, At("2026-11-09T00:00:00Z"));
        Assert.True(m[1].DueAtActivation);
    }

    [Fact]
    public void Checkout_always_charges_min_of_deposit_and_hire_price()
    {
        Assert.Equal(1000m, PaymentScheduleService.CheckoutAmount(1000, 4060));
        Assert.Equal(880m, PaymentScheduleService.CheckoutAmount(1000, 880));
    }

    [Fact]
    public void Amount_due_now_is_the_largest_open_shortfall_else_the_amount_owing()
    {
        var booking = TestBookings.Staff("2027-03-08", "2027-03-12", hire: 4060, now: At("2026-11-09T00:00:00Z"));
        Assert.Equal(1000m, schedule.AmountDueNow(booking, At("2026-11-09T00:00:00Z")));
        booking.RecordPayment(TestBookings.Paid(1000), At("2026-11-10T00:00:00Z"));
        Assert.Equal(3060m, schedule.AmountDueNow(booking, At("2026-11-10T00:00:00Z")));
        Assert.Equal(3060m, schedule.AmountDueNow(booking, At("2027-02-01T00:00:00Z")));
    }

    [Fact]
    public void Amount_due_now_covers_a_priced_addon_on_a_paid_booking()
    {
        var booking = TestBookings.Staff("2027-03-08", "2027-03-12", hire: 4060, lines: [TestBookings.Line("hamper", 1, 150)]);
        booking.RecordPayment(TestBookings.Paid(4060), TestBookings.Now);
        Assert.Equal(150m, schedule.AmountDueNow(booking, TestBookings.Now));
    }

    [Fact]
    public void Deposit_above_hire_price_requires_only_the_total()
    {
        var m = schedule.Milestones(PaymentScheduleType.Standard, D("2027-03-08"), 1000, 880, At("2026-11-09T00:00:00Z"));
        Assert.Equal(880m, m[0].RequiredCumulative);
    }
}
