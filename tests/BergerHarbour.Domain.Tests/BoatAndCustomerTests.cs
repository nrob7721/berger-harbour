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
    public void Rooming_warning_when_guests_exceed_adult_plus_kids_beds(int guests, bool expected)
    {
        var boat = CompleteBoat(Seasons(), adultBeds: 6, kidsBeds: 2);
        Assert.Equal(8, boat.TotalBeds);
        Assert.Equal(expected, boat.RequiresRoomingWarning(guests));
    }

    [Fact]
    public void Rooming_warning_counts_a_boat_without_kids_beds()
    {
        var boat = CompleteBoat(Seasons(), adultBeds: 4, kidsBeds: 0);
        Assert.False(boat.RequiresRoomingWarning(4));
        Assert.True(boat.RequiresRoomingWarning(5));
    }

    [Fact]
    public void Rooming_warning_text_describes_the_bedding()
    {
        var boat = CompleteBoat(Seasons(), adultBeds: 6, kidsBeds: 2);
        Assert.Equal(
            "This boat has 6 adult beds and 2 children's beds (4 queen bedrooms, bunk area (2 singles), single in lounge, " +
            "single in dining area). With 9 guests, some guests will need to share beds. Please make sure your group's " +
            "sleeping arrangements suit this layout.",
            boat.RoomingWarningText(9));
    }

    [Fact]
    public void Boat_deposit_falls_back_to_the_settings_default()
    {
        Assert.Equal(1000m, CompleteBoat(Seasons()).EffectiveDeposit(1000m));
        Assert.Equal(1500m, CompleteBoat(Seasons(), deposit: 1500m).EffectiveDeposit(1000m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Boat_deposit_must_be_positive_when_set(int deposit) =>
        Assert.Throws<DomainValidationException>(() => CompleteBoat(Seasons(), deposit: deposit));

    [Fact]
    public void Beds_must_be_sensible()
    {
        Assert.Throws<DomainValidationException>(() => CompleteBoat(Seasons(), adultBeds: 0));
        Assert.Throws<DomainValidationException>(() => CompleteBoat(Seasons(), kidsBeds: -1));
    }

    [Fact]
    public void Boats_are_listed_largest_first_then_by_name()
    {
        var boats = new[]
        {
            Boat.Create("Wanderer", "wanderer", BoatType.House, 6, Now),
            Boat.Create("Pacific Blue", "pacific-blue", BoatType.House, 12, Now),
            Boat.Create("Misty Blue", "misty-blue", BoatType.House, 9, Now),
            Boat.Create("Kalinda", "kalinda", BoatType.House, 12, Now),
        };
        Assert.Equal(["Kalinda", "Pacific Blue", "Misty Blue", "Wanderer"], Boat.InDisplayOrder(boats).Select(b => b.Name));
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
        Assert.Contains("Number of adult beds", ex.Message);
        Assert.Contains("Number of kids beds", ex.Message);
        Assert.Contains("Peak Week rate", ex.Message);

        var missing = boat.MissingActivationData(seasons);
        Assert.Equal(3 + seasons.Count * 3, missing.Count);
    }

    [Fact]
    public void Zero_kids_beds_is_complete()
    {
        var seasons = Seasons();
        var boat = CompleteBoat(seasons, kidsBeds: 0);
        boat.Activate(seasons, Now);
        Assert.True(boat.IsActive);
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
        boat.UpdateDetails("BBQ 1", "bbq-1", 8, 1, 0, "None", 0m, null, Now);
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
        Assert.Equal(expected, Customer.Create("Jane", raw, "0412 345 678", Now).Email.Value);

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
        var customer = Customer.Create("Jane", "jane@example.com", "0412 345 678", Now);
        Assert.True(customer.UpdateContactDetails("Jane Smith", null, Now));
        Assert.Equal("Jane Smith", customer.FullName);
        Assert.Equal("+61412345678", customer.MobileNumber);
        Assert.True(customer.UpdateContactDetails("Jane Smith", "+44 7911 123456", Now));
        Assert.Equal("+447911123456", customer.MobileNumber);
        Assert.False(customer.UpdateContactDetails("Jane Smith", "+447911123456", Now));
    }

    [Fact]
    public void Secondary_phone_starts_empty_and_is_set_by_staff()
    {
        var customer = Customer.Create("Jane", "jane@example.com", null, Now);
        Assert.Null(customer.SecondaryPhone);
        Assert.True(customer.ChangeSecondaryPhone("(02) 4444 4444", Now));
        Assert.Equal("+61244444444", customer.SecondaryPhone);
        Assert.False(customer.ChangeSecondaryPhone("+61244444444", Now));
        Assert.True(customer.ChangeSecondaryPhone(null, Now));
        Assert.Null(customer.SecondaryPhone);
        Assert.Throws<DomainValidationException>(() => customer.ChangeSecondaryPhone("123", Now));
    }

    [Theory]
    [InlineData("0412 345 678", "+61412345678")]
    [InlineData("+61 412 345 678", "+61412345678")]
    [InlineData("(02) 4444 4444", "+61244444444")]
    [InlineData("+44 7911 123456", "+447911123456")]
    [InlineData("+64 21 123 4567", "+64211234567")]
    public void Phone_numbers_are_stored_in_e164(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalise(raw, "mobile", "Mobile number"));

    [Theory]
    [InlineData("0400")]
    [InlineData("12345")]
    [InlineData("+44 12")]
    [InlineData("not a number")]
    public void Invalid_phone_numbers_are_rejected(string raw)
    {
        Assert.False(PhoneNumber.IsValid(raw));
        Assert.Throws<DomainValidationException>(() => PhoneNumber.Normalise(raw, "mobile", "Mobile number"));
    }

    [Theory]
    [InlineData("+61412345678", "0412 345 678")]
    [InlineData("+447911123456", "+44 7911 123456")]
    [InlineData("legacy 0400", "legacy 0400")]
    public void Phone_numbers_display_nationally_for_australia_and_internationally_otherwise(string stored, string expected) =>
        Assert.Equal(expected, PhoneNumber.Display(stored));

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
    public void Email_template_sections_must_be_known_and_closed()
    {
        var template = EmailTemplate.Create(EmailTemplateKey.BookingConfirmed, "Booking {{BookingReference}}",
            "<p>{{#PaymentDueNow}}Pay {{AmountDue}} at {{PaymentLink}}{{/PaymentDueNow}}</p>");
        Assert.Throws<DomainValidationException>(() => template.Update("Hi", "<p>{{#PaymentDueNow}}Pay now</p>"));
        Assert.Throws<DomainValidationException>(() => template.Update("Hi", "<p>{{#Nope}}x{{/Nope}}</p>"));
    }

    [Theory]
    [InlineData(true, "<p>A Pay $1 B</p>")]
    [InlineData(false, "<p>A  B</p>")]
    public void Email_template_sections_are_kept_only_when_their_condition_holds(bool holds, string expected) =>
        Assert.Equal(expected, EmailPlaceholders.ApplySections("<p>A {{#PaymentDueNow}}Pay $1{{/PaymentDueNow}} B</p>",
            name => name == "PaymentDueNow" && holds));

    [Fact]
    public void Business_settings_online_window()
    {
        var settings = BusinessSettings.CreateDefault(D(2027, 11, 30), "staff@example.com", "02 0000 0000", "info@example.com");
        settings.Update(500m, 30, D(2027, 11, 30), "staff@example.com", "02 0000 0000", "info@example.com",
            BusinessSettings.DefaultHireTermsUrl);
        var today = D(2026, 10, 5);
        Assert.False(settings.SatisfiesLeadTime(new Stay(D(2026, 11, 3), D(2026, 11, 6)), today));
        Assert.True(settings.SatisfiesLeadTime(new Stay(D(2026, 11, 4), D(2026, 11, 7)), today));
        // Last night 30/11/2027 is open; a stay with last night 01/12 is not.
        Assert.True(settings.IsOpenForOnlineBooking(new Stay(D(2027, 11, 26), D(2027, 12, 1))));
        Assert.False(settings.IsOpenForOnlineBooking(new Stay(D(2027, 11, 26), D(2027, 12, 2))));
    }
}
