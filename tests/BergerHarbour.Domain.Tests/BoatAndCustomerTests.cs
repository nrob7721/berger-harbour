using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class BoatAndCustomerTests
{
    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(12, true)]
    public void Rooming_warning_when_guests_exceed_beds(int guests, bool expected)
    {
        var boat = CompleteBoat(Seasons(), beds: 8);
        Assert.Equal(expected, boat.RequiresRoomingWarning(guests));
    }

    [Fact]
    public void Rooming_warning_text_describes_the_bedding()
    {
        var boat = CompleteBoat(Seasons(), beds: 8);
        Assert.Equal(
            "This boat has 8 beds (4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area). " +
            "With 9 guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout.",
            boat.RoomingWarningText(9));
    }

    [Fact]
    public void New_boats_are_inactive()
    {
        var boat = Boat.Create("Kalinda", "kalinda", BoatType.House, 12, Now);
        Assert.False(boat.IsActive);
    }

    [Fact]
    public void Activation_requires_beds_bedding_and_every_season_rate()
    {
        var seasons = Seasons();
        var boat = Boat.Create("Kalinda", "kalinda", BoatType.House, 12, Now);
        var ex = Assert.Throws<DomainValidationException>(() => boat.Activate(seasons, Now));
        Assert.Contains("Number of beds", ex.Message);
        Assert.Contains("Peak Week rate", ex.Message);

        var missing = boat.MissingActivationData(seasons);
        Assert.Equal(2 + seasons.Count * 3, missing.Count);
    }

    [Fact]
    public void Long_weekend_rate_is_not_required_for_activation()
    {
        var seasons = Seasons();
        var boat = CompleteBoat(seasons);
        boat.SetRates(boat.Rates.Where(r => r.PeriodType != PeriodType.LongWeekend), Now);
        boat.Activate(seasons, Now);
        Assert.True(boat.IsActive);
    }

    [Fact]
    public void Complete_boat_activates_and_must_stay_complete()
    {
        var seasons = Seasons();
        var boat = CompleteBoat(seasons);
        boat.Activate(seasons, Now);
        Assert.True(boat.IsActive);

        boat.SetRates(boat.Rates.Where(r => r.PeriodType != PeriodType.Week), Now);
        Assert.Throws<DomainValidationException>(() => boat.EnsureStillComplete(seasons));
        boat.Deactivate(Now);
        boat.EnsureStillComplete(seasons);
    }

    [Fact]
    public void Bbq_boats_cannot_be_activated()
    {
        var seasons = Seasons();
        var boat = Boat.Create("BBQ 1", "bbq-1", BoatType.BBQ, 8, Now);
        boat.UpdateDetails("BBQ 1", "bbq-1", 8, 1, "None", 0m, Now);
        Assert.Throws<DomainValidationException>(() => boat.Activate(seasons, Now));
    }

    [Theory]
    [InlineData("pacific-blue", true)]
    [InlineData("paradise-ii", true)]
    [InlineData("Pacific-Blue", false)]
    [InlineData("pacific blue", false)]
    [InlineData("-pacific", false)]
    [InlineData("pacific--blue", false)]
    public void Slug_must_be_kebab_case(string slug, bool valid)
    {
        if (valid)
        {
            Assert.Equal(slug, Boat.Create("X", slug, BoatType.House, 4, Now).Slug);
        }
        else
        {
            Assert.Throws<DomainValidationException>(() => Boat.Create("X", slug, BoatType.House, 4, Now));
        }
    }

    [Fact]
    public void Rates_must_be_unique_per_season_and_period()
    {
        var boat = Boat.Create("X", "x", BoatType.House, 4, Now);
        Assert.Throws<DomainValidationException>(() => boat.SetRates(
            [new BoatRate(SeasonId.Normal, PeriodType.Week, 1m), new BoatRate(SeasonId.Normal, PeriodType.Week, 2m)], Now));
    }

    [Fact]
    public void Custom_is_not_a_rated_period() =>
        Assert.Throws<DomainValidationException>(() => new BoatRate(SeasonId.Normal, PeriodType.Custom, 1m));

    [Theory]
    [InlineData("  Jane@Example.COM ", "jane@example.com")]
    [InlineData("a.b+c@sub.example.com.au", "a.b+c@sub.example.com.au")]
    public void Customer_email_is_normalised(string raw, string expected) =>
        Assert.Equal(expected, Customer.Create("Jane", raw, "0400 000 000", Now).Email.Value);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    public void Customer_email_is_mandatory_and_valid(string raw) =>
        Assert.Throws<DomainValidationException>(() => Customer.Create("Jane", raw, null, Now));

    [Fact]
    public void Booking_again_updates_name_and_mobile_but_keeps_mobile_when_not_given()
    {
        var customer = Customer.Create("Jane", "jane@example.com", "0400", Now);
        Assert.True(customer.UpdateContactDetails("Jane Smith", null, Now));
        Assert.Equal("Jane Smith", customer.FullName);
        Assert.Equal("0400", customer.MobileNumber);
        Assert.True(customer.UpdateContactDetails("Jane Smith", "0411", Now));
        Assert.Equal("0411", customer.MobileNumber);
        Assert.False(customer.UpdateContactDetails("Jane Smith", "0411", Now));
    }

    [Fact]
    public void Email_template_rejects_unknown_placeholders()
    {
        var template = EmailTemplate.Create(EmailTemplateKey.PaymentDue, "Payment due for {{BookingReference}}",
            "<p>Pay {{AmountDue}} at {{PaymentLink}}</p>");
        var ex = Assert.Throws<DomainValidationException>(() => template.Update("Hi", "<p>{{Nope}} {{ CustomerName }}</p>"));
        Assert.Contains("{{Nope}}", ex.Message);
        Assert.Equal("htmlBody", ex.Field);
    }

    [Fact]
    public void Business_settings_online_window()
    {
        var settings = BusinessSettings.CreateDefault(D(2027, 11, 30), "staff@example.com", "02 0000 0000", "info@example.com");
        var today = D(2026, 10, 5);
        Assert.False(settings.SatisfiesLeadTime(new Stay(D(2026, 11, 3), D(2026, 11, 6)), today));
        Assert.True(settings.SatisfiesLeadTime(new Stay(D(2026, 11, 4), D(2026, 11, 7)), today));
        // Last night 30/11/2027 is open; a stay with last night 01/12 is not.
        Assert.True(settings.IsOpenForOnlineBooking(new Stay(D(2027, 11, 26), D(2027, 12, 1))));
        Assert.False(settings.IsOpenForOnlineBooking(new Stay(D(2027, 11, 26), D(2027, 12, 2))));
    }
}
