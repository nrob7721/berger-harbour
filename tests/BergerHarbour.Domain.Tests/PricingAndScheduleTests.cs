using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class PricingTests
{
    [Theory]
    [InlineData("2027-03-08", "Normal")]
    [InlineData("2027-05-01", "Off Peak")]
    [InlineData("2027-08-31", "Off Peak")]
    [InlineData("2027-09-01", "Normal")]
    [InlineData("2027-11-30", "Normal")]
    [InlineData("2027-12-01", "Peak")]
    [InlineData("2028-01-31", "Peak")] // wraps the year end
    [InlineData("2028-02-01", "Normal")]
    public void Season_is_the_one_containing_the_start_date(string start, string season) =>
        Assert.Equal(season, PricingService.SeasonFor(D(start), Seasons()).Name);

    [Fact]
    public void Hire_price_is_the_boat_rate_for_season_and_period()
    {
        var boat = Boat();
        Assert.Equal(4060m, PricingService.HirePrice(boat, Seasons(), PeriodType.Midweek, D("2027-03-08")));
        Assert.Equal(3570m, PricingService.HirePrice(boat, Seasons(), PeriodType.Weekend, D("2027-06-04")));
        Assert.Equal(6200m, PricingService.HirePrice(boat, Seasons(), PeriodType.Week, D("2028-01-07")));
        Assert.Equal(5100m, PricingService.HirePrice(boat, Seasons(), PeriodType.LongWeekend, D("2027-12-03")));
        Assert.Null(PricingService.HirePrice(boat, Seasons(), PeriodType.Custom, D("2027-12-03")));
    }

    [Fact]
    public void Hire_price_is_snapshotted_on_the_booking()
    {
        var boat = Boat();
        var booking = StaffBooking("2027-03-08", "2027-03-12", hire: PricingService.HirePrice(boat, Seasons(), PeriodType.Midweek, D("2027-03-08"))!.Value);
        boat.SetRates(Rates("normal", 1, 1, 1, 1));
        Assert.Equal(4060m, booking.HirePrice);
    }

    private static AddonDefinition Addon(bool priceOnRequest = false) => AddonDefinition.Create(
        new AddonDetails("Outboard motor", null, priceOnRequest, 95, 95, 130, false, null, null), 0, Now);

    [Fact]
    public void Addon_price_follows_the_period_type()
    {
        var a = Addon();
        Assert.Equal(95m, PricingService.AddonUnitPrice(a, PeriodType.Midweek));
        Assert.Equal(95m, PricingService.AddonUnitPrice(a, PeriodType.Weekend));
        Assert.Equal(130m, PricingService.AddonUnitPrice(a, PeriodType.Week));
    }

    [Fact]
    public void No_addon_prices_for_long_weekend_or_custom_bookings()
    {
        var a = Addon();
        Assert.Null(PricingService.AddonUnitPrice(a, PeriodType.LongWeekend));
        Assert.Null(PricingService.AddonUnitPrice(a, PeriodType.Custom));
    }

    [Fact]
    public void Price_on_request_has_no_price() => Assert.Null(PricingService.AddonUnitPrice(Addon(priceOnRequest: true), PeriodType.Midweek));
}

public class PaymentScheduleTests
{
    private static readonly PaymentScheduleService Service = new(PaymentScheduleOptions.Default);

    [Fact]
    public void Standard_schedule_has_a_deposit_and_a_balance_30_days_before_11am_check_in()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12", hire: 4000, deposit: 1000);
        var m = Service.Milestones(b);
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.Balance], m.Select(x => x.Kind));
        Assert.Equal(1000m, m[0].RequiredCumulative);
        Assert.Equal(4000m, m[1].RequiredCumulative);
        Assert.Equal(SydneyTime.ToInstant(D("2027-02-06"), new TimeOnly(11, 0)), m[1].DueAt);
        Assert.Equal(m[1].DueAt.AddDays(-7), m[1].ReminderAt);
    }

    [Fact]
    public void Extended_schedule_has_50_percent_at_120_days_and_100_percent_at_90_days()
    {
        var b = StaffBooking("2026-12-21", "2026-12-28", hire: 8600, schedule: PaymentScheduleType.Extended);
        var m = Service.Milestones(b);
        Assert.Equal([MilestoneKind.Deposit, MilestoneKind.First, MilestoneKind.Final], m.Select(x => x.Kind));
        Assert.Equal(4300m, m[1].RequiredCumulative);
        Assert.Equal(8600m, m[2].RequiredCumulative);
        var checkIn = SydneyTime.CheckInInstant(D("2026-12-21"));
        Assert.Equal(checkIn.AddDays(-120), m[1].DueAt);
        Assert.Equal(checkIn.AddDays(-90), m[2].DueAt);
    }

    [Fact]
    public void Extended_schedule_applies_when_the_stay_overlaps_a_flagged_blocked_period()
    {
        var christmas = BlockedPeriod.Create("Christmas", D("2026-12-20"), D("2027-01-05"), true, Now);
        var easter = BlockedPeriod.Create("Easter", D("2027-03-26"), D("2027-03-29"), false, Now);
        Assert.Equal(PaymentScheduleType.Extended, PaymentScheduleService.DetermineSchedule(Stay("2026-12-17", "2026-12-21"), [christmas, easter]));
        Assert.Equal(PaymentScheduleType.Standard, PaymentScheduleService.DetermineSchedule(Stay("2027-03-26", "2027-03-29"), [christmas, easter]));
        Assert.Equal(PaymentScheduleType.Standard, PaymentScheduleService.DetermineSchedule(Stay("2026-12-14", "2026-12-18"), [christmas]));
    }

    [Fact]
    public void Checkout_charges_min_of_deposit_and_hire_price()
    {
        Assert.Equal(1000m, PaymentScheduleService.CheckoutAmount(1000, 4060));
        Assert.Equal(880m, PaymentScheduleService.CheckoutAmount(1000, 880)); // deposit > hire price
    }

    [Fact]
    public void Checkout_charges_only_the_deposit_even_when_the_balance_is_already_due()
    {
        // Booked 10 days before check-in: the balance is due at creation, but checkout still takes only the deposit.
        var created = SydneyTime.CheckInInstant(D("2026-10-11"));
        var b = StaffBooking("2026-10-21", "2026-10-25", hire: 4000, createdAt: created);
        Assert.True(Service.Milestones(b)[1].WasDueAtCreation(b.CreatedDate));
        Assert.Equal(1000m, PaymentScheduleService.CheckoutAmount(b.DepositAmount, b.HirePrice));
    }

    [Fact]
    public void Amount_due_now_is_the_largest_open_shortfall_else_the_amount_owing()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12", hire: 4000, deposit: 1000, createdAt: Now);
        Assert.Equal(1000m, Service.AmountDueNow(b, Now)); // only the deposit window is open
        b.RecordPayment(new Payment("p1", 1000, PaymentMethod.Manual, null, Now, null, BookingCreator.SystemUser));
        Assert.Equal(3000m, Service.AmountDueNow(b, Now)); // nothing open: falls back to the amount owing
        var reminder = Service.Milestones(b)[1].ReminderAt;
        Assert.Equal(3000m, Service.AmountDueNow(b, reminder));
        b.RecordPayment(new Payment("p2", 3000, PaymentMethod.Manual, null, Now, null, BookingCreator.SystemUser));
        Assert.Equal(0m, Service.AmountDueNow(b, reminder));
    }

    [Fact]
    public void Per_boat_deposit_falls_back_to_the_settings_default()
    {
        Assert.Equal(1000m, Boat(deposit: null).EffectiveDeposit(1000));
        Assert.Equal(250m, Boat(deposit: 250).EffectiveDeposit(1000));
    }
}

public class BookingTotalsTests
{
    private static Payment Paid(decimal amount) => new(Ids.New(), amount, PaymentMethod.Manual, null, Now, null, BookingCreator.SystemUser);

    [Fact]
    public void Total_counts_priced_lines_and_excludes_unpriced_ones()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12", hire: 4000, lines: [Line("motor", 1, 95), Line("ice", 3, 5), Line("hamper", 1)]);
        Assert.Equal(4110m, b.TotalPrice);
        Assert.Equal(1, b.UnpricedAddonLineCount);
    }

    [Fact]
    public void Payment_status_is_derived_in_order()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12", hire: 4000, deposit: 1000);
        Assert.Equal(PaymentStatus.Outstanding, b.PaymentStatus);
        b.RecordPayment(Paid(500));
        Assert.Equal(PaymentStatus.PartPaid, b.PaymentStatus); // below the deposit
        b.RecordPayment(Paid(500));
        Assert.Equal(PaymentStatus.DepositPaid, b.PaymentStatus); // equal to the deposit
        b.RecordPayment(Paid(1000));
        Assert.Equal(PaymentStatus.PartPaid, b.PaymentStatus);
        b.RecordPayment(Paid(2000));
        Assert.Equal(PaymentStatus.FullyPaid, b.PaymentStatus);
        Assert.Equal(0m, b.AmountOwing);
    }

    [Fact]
    public void Stripe_payments_are_idempotent_on_payment_intent()
    {
        var b = StaffBooking("2027-03-08", "2027-03-12");
        var p = new Payment("p1", 1000, PaymentMethod.Stripe, "pi_1", Now, null, BookingCreator.Customer);
        Assert.True(b.RecordPayment(p));
        Assert.False(b.RecordPayment(p with { Id = "p2" }));
        Assert.Equal(1000m, b.AmountPaid);
    }
}
