using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class PaymentScheduleTests
{
    private readonly PaymentScheduleService _service = new(PaymentScheduleOffsets.Default);

    private static readonly BlockedPeriod Christmas =
        BlockedPeriod.Create("Christmas / New Year 2026–27", D(2026, 12, 20), D(2027, 1, 5), true);

    private static readonly BlockedPeriod Easter = BlockedPeriod.Create("Easter 2027", D(2027, 3, 26), D(2027, 3, 28), false);

    [Fact]
    public void Standard_unless_the_stay_overlaps_an_extended_blocked_period()
    {
        Assert.Equal(PaymentSchedule.Standard,
            PaymentScheduleService.DetermineSchedule(new Stay(D(2026, 12, 14), D(2026, 12, 18)), [Christmas, Easter]));
        Assert.Equal(PaymentSchedule.Extended,
            PaymentScheduleService.DetermineSchedule(new Stay(D(2026, 12, 17), D(2026, 12, 21)), [Christmas, Easter]));
        Assert.Equal(PaymentSchedule.Standard,
            PaymentScheduleService.DetermineSchedule(new Stay(D(2027, 3, 26), D(2027, 3, 29)), [Christmas, Easter]));
    }

    [Fact]
    public void Check_in_is_1pm_sydney_time()
    {
        // 7 December 2026 is daylight saving (AEDT, UTC+11).
        Assert.Equal(new DateTimeOffset(2026, 12, 7, 2, 0, 0, TimeSpan.Zero), SydneyTime.CheckInInstant(D(2026, 12, 7)));
        // 7 July 2027 is standard time (AEST, UTC+10).
        Assert.Equal(new DateTimeOffset(2027, 7, 7, 3, 0, 0, TimeSpan.Zero), SydneyTime.CheckInInstant(D(2027, 7, 7)));
    }

    [Fact]
    public void Standard_schedule_milestones()
    {
        var milestones = _service.Milestones(PaymentSchedule.Standard, D(2026, 12, 7), 4060m, 1000m, Now);
        Assert.Collection(milestones,
            m =>
            {
                Assert.Equal(MilestoneKind.Deposit, m.Kind);
                Assert.Equal(1000m, m.RequiredCumulative);
                Assert.Equal(Now, m.DueAt);
            },
            m =>
            {
                Assert.Equal(MilestoneKind.Balance, m.Kind);
                Assert.Equal(4060m, m.RequiredCumulative);
                Assert.Equal(SydneyTime.CheckInInstant(D(2026, 12, 7)).AddDays(-30), m.DueAt);
                Assert.Equal(D(2026, 11, 7), m.DueDate);
            });
    }

    [Fact]
    public void Extended_schedule_has_cumulative_50_and_100_percent_milestones()
    {
        var milestones = _service.Milestones(PaymentSchedule.Extended, D(2026, 12, 20), 6201m, 1000m, Now);
        var checkIn = SydneyTime.CheckInInstant(D(2026, 12, 20));
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.FirstInstalment, MilestoneKind.Final], milestones.Select(m => m.Kind));
        Assert.Equal(3100.50m, milestones[1].RequiredCumulative);
        Assert.Equal(checkIn.AddDays(-120), milestones[1].DueAt);
        Assert.Equal(6201m, milestones[2].RequiredCumulative);
        Assert.Equal(checkIn.AddDays(-90), milestones[2].DueAt);
    }

    [Fact]
    public void Deposit_milestone_is_capped_at_the_total()
    {
        var milestones = _service.Milestones(PaymentSchedule.Standard, D(2026, 12, 7), 800m, 1000m, Now);
        Assert.Equal(800m, milestones[0].RequiredCumulative);
    }

    [Fact]
    public void Amount_due_now_is_the_deposit_on_an_unpaid_booking_before_the_balance_window()
    {
        var booking = StaffBooking(BoatId.New(), D(2027, 3, 1), D(2027, 3, 5));
        Assert.Equal(1000m, _service.AmountDueNow(booking, Now));
    }

    [Fact]
    public void Amount_due_now_is_the_amount_owing_when_no_milestone_is_short()
    {
        var booking = StaffBooking(BoatId.New(), D(2027, 3, 1), D(2027, 3, 5));
        booking.RecordManualPayment(1000m, Now, null, Now);
        Assert.Equal(3060m, _service.AmountDueNow(booking, Now));
    }

    [Fact]
    public void Amount_due_now_is_the_balance_shortfall_once_the_reminder_window_opens()
    {
        var booking = StaffBooking(BoatId.New(), D(2026, 12, 7), D(2026, 12, 11));
        booking.RecordManualPayment(1000m, Now, null, Now);
        var balanceDue = SydneyTime.CheckInInstant(D(2026, 12, 7)).AddDays(-30);
        Assert.Equal(3060m, _service.AmountDueNow(booking, balanceDue.AddDays(-7)));
    }

    [Fact]
    public void Extended_amount_due_now_follows_the_cumulative_milestones()
    {
        var booking = StaffBooking(BoatId.New(), D(2027, 12, 20), D(2027, 12, 27), hirePrice: 6000m,
            schedule: PaymentSchedule.Extended);
        booking.RecordManualPayment(1000m, Now, null, Now);
        var checkIn = SydneyTime.CheckInInstant(D(2027, 12, 20));

        // Before the first instalment window: nothing short, so the whole balance may be paid.
        Assert.Equal(5000m, _service.AmountDueNow(booking, checkIn.AddDays(-128)));
        // First instalment window open: 50% of 6000 = 3000 required, 1000 paid.
        Assert.Equal(2000m, _service.AmountDueNow(booking, checkIn.AddDays(-127)));

        booking.RecordManualPayment(2000m, Now, null, Now);
        // First instalment met, final not open yet → the remaining amount owing.
        Assert.Equal(3000m, _service.AmountDueNow(booking, checkIn.AddDays(-100)));
        // Final window open.
        Assert.Equal(3000m, _service.AmountDueNow(booking, checkIn.AddDays(-97)));

        booking.RecordManualPayment(3000m, Now, null, Now);
        Assert.Equal(0m, _service.AmountDueNow(booking, checkIn.AddDays(-97)));
    }

    [Fact]
    public void Online_checkout_charges_the_deposit_when_the_balance_is_not_yet_due()
    {
        // Now is 05/10/2026 11:00 Sydney. Balance for a 05/11 check-in is due 06/10.
        Assert.Equal(1000m, _service.OnlineCheckoutAmount(PaymentSchedule.Standard, D(2026, 11, 5), 4060m, 1000m, Now));
    }

    [Fact]
    public void Online_checkout_charges_the_full_hire_price_when_the_balance_is_due_today()
    {
        // Balance for a 04/11 check-in is due 05/10 13:00 Sydney — today, even though that instant is still ahead.
        Assert.Equal(4060m, _service.OnlineCheckoutAmount(PaymentSchedule.Standard, D(2026, 11, 4), 4060m, 1000m, Now));
        Assert.Equal(4060m, _service.OnlineCheckoutAmount(PaymentSchedule.Standard, D(2026, 10, 20), 4060m, 1000m, Now));
    }

    [Fact]
    public void Online_checkout_never_charges_more_than_the_hire_price() =>
        Assert.Equal(800m, _service.OnlineCheckoutAmount(PaymentSchedule.Standard, D(2027, 3, 1), 800m, 1000m, Now));

    [Fact]
    public void Shortened_offsets_for_testing()
    {
        var service = new PaymentScheduleService(new PaymentScheduleOffsets(
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)));
        var milestones = service.Milestones(PaymentSchedule.Standard, D(2026, 12, 7), 4060m, 1000m, Now);
        Assert.Equal(SydneyTime.CheckInInstant(D(2026, 12, 7)).AddMinutes(-30), milestones[1].DueAt);
        Assert.Equal(milestones[1].DueAt.AddMinutes(-10), service.ReminderAt(milestones[1]));
    }
}
