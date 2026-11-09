using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.TestData;

namespace BergerHarbour.Domain.Tests;

public class BoatTests
{
    [Fact]
    public void Activation_needs_beds_bedding_and_every_season_rate()
    {
        var boat = Domain.Boats.Boat.Create(Details(adultBeds: null, kidsBeds: null, bedding: null), Now);
        var missing = boat.MissingForActivation(Seasons());
        Assert.Contains("Number of adult beds", missing);
        Assert.Contains("Bedding description", missing);
        Assert.Contains("Rate: Off Peak – Week", missing);
        var e = Assert.Throws<DomainValidationException>(() => boat.Activate(Seasons()));
        Assert.True(e.Errors.ContainsKey("isActive"));
    }

    [Fact]
    public void Activation_does_not_need_image_spa_or_pets_and_allows_zero_kids_beds()
    {
        var boat = Domain.Boats.Boat.Create(Details(kidsBeds: 0, imageUrl: null) with { HasSpa = false, IsPetFriendly = false }, Now);
        boat.SetRates(Rates("normal", 1, 1, 1, 1).Concat(Rates("off-peak", 1, 1, 1, 1)).Concat(Rates("peak", 1, 1, 1, 1)));
        boat.Activate(Seasons());
        Assert.True(boat.IsActive);
    }

    [Fact]
    public void Long_weekend_rates_are_not_needed_to_activate()
    {
        var boat = Domain.Boats.Boat.Create(Details(), Now);
        boat.SetRates(Seasons().SelectMany(s => Domain.Boats.Boat.RequiredRatePeriods.Select(p => new Domain.Boats.BoatRate(s.Id, p, 100))));
        boat.Activate(Seasons());
        Assert.True(boat.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Berth_must_be_at_least_adult_plus_kids_beds_on_create_and_edit(bool active)
    {
        var e = Assert.Throws<DomainValidationException>(() => Domain.Boats.Boat.Create(Details(berth: 7, adultBeds: 6, kidsBeds: 2), Now));
        Assert.Equal(["Berth can't be less than adult beds + kids beds (8)."], e.Errors["berth"]);
        var boat = Boat(active);
        var edit = Assert.Throws<DomainValidationException>(() => boat.UpdateDetails(Details(berth: 12, adultBeds: 10, kidsBeds: 4)));
        Assert.True(edit.Errors.ContainsKey("berth"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", true)]
    [InlineData("https://www.bergerhouseboats.com.au/a.jpg", true)]
    [InlineData("http://bergerhouseboats.com.au/a.jpg", false)]
    [InlineData("https://evil.example.com/a.jpg", false)]
    [InlineData("https://bergerhouseboats.com.au.evil.com/a.jpg", false)]
    [InlineData("/wp-content/a.jpg", false)]
    public void Image_url_must_be_https_on_the_wordpress_site(string? url, bool valid) =>
        Assert.Equal(valid, Domain.Boats.Boat.IsValidImageUrl(url));

    [Fact]
    public void Slug_must_be_kebab_case() =>
        Assert.Throws<DomainValidationException>(() => Domain.Boats.Boat.Create(Details() with { Slug = "Pacific Blue" }, Now));
}

public class ContactTests
{
    [Fact]
    public void Email_is_normalised() => Assert.Equal("jane@example.com", EmailAddress.Require("  Jane@Example.COM "));

    [Theory]
    [InlineData("jane@gmail.com")]
    [InlineData("jane@bigpond.net.au")]
    [InlineData("jane@example.co.uk")]
    [InlineData("  jane.smith+boats@my-domain.com.au  ")]
    public void Valid_emails_are_accepted(string email) => Assert.True(EmailAddress.IsValid(email));

    [Theory]
    [InlineData("jane@gmail")] // no top-level domain
    [InlineData("jane@b.c")] // one-letter top-level domain
    [InlineData("jane@gmail..com")] // empty label
    [InlineData("jane@-gmail.com")] // label starts with a hyphen
    [InlineData("jane@gmail-.com")] // label ends with a hyphen
    [InlineData("jane@gmail.c0m")] // numeric top-level domain
    [InlineData("jane smith@gmail.com")] // space in the local part
    [InlineData("@gmail.com")]
    [InlineData("jane@@gmail.com")]
    [InlineData("jane@x@gmail.com")]
    [InlineData("jane@gm_ail.com")]
    [InlineData("")]
    public void Invalid_emails_are_rejected(string email)
    {
        Assert.False(EmailAddress.IsValid(email));
        if (email.Length > 0)
            Assert.Equal([EmailAddress.InvalidMessage], Assert.Throws<DomainValidationException>(() => EmailAddress.Require(email)).Errors["email"]);
    }

    [Fact]
    public void Emails_over_254_characters_are_rejected()
    {
        var domain = "@example.com";
        Assert.True(EmailAddress.IsValid(new string('a', 254 - domain.Length) + domain));
        Assert.False(EmailAddress.IsValid(new string('a', 255 - domain.Length) + domain));
    }

    [Fact]
    public void Address_is_trimmed_blank_is_stored_empty_and_over_500_characters_is_rejected()
    {
        var c = Customer.Create("Jane", "Smith", "jane@example.com", null, Now);
        Assert.Null(c.Address);
        c.UpdateContactDetails("Jane", "Smith", null, null, "  1 River Rd\nMannum SA 5238 \n");
        Assert.Equal("1 River Rd\nMannum SA 5238", c.Address);
        c.UpdateContactDetails("Jane", "Smith", null, null, "   ");
        Assert.Null(c.Address);
        c.UpdateContactDetails("Jane", "Smith", null, null, new string('a', 500));
        Assert.Equal(500, c.Address!.Length);
        var e = Assert.Throws<DomainValidationException>(() => c.UpdateContactDetails("Jane", "Smith", null, null, new string('a', 501)));
        Assert.True(e.Errors.ContainsKey("address"));
        Assert.Equal(500, c.Address!.Length); // unchanged
    }

    [Theory]
    [InlineData("0412 345 678", "+61412345678")]
    [InlineData("+61 412 345 678", "+61412345678")]
    [InlineData("+1 650 253 0000", "+16502530000")] // FIXED_LINE_OR_MOBILE (US)
    [InlineData("+44 7911 123456", "+447911123456")]
    public void Mobiles_are_stored_in_e164(string input, string expected)
    {
        Assert.True(MobileNumber.TryNormalise(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("02 9876 5432")] // Australian landline
    [InlineData("+44 20 7946 0958")] // UK landline
    [InlineData("12345")]
    [InlineData("")]
    public void Landlines_and_junk_are_rejected(string input) => Assert.False(MobileNumber.TryNormalise(input, out _));

    [Fact]
    public void Display_is_national_for_australia_and_international_otherwise()
    {
        Assert.Equal("0412 345 678", MobileNumber.Display("+61412345678"));
        Assert.Equal("+44 7911 123456", MobileNumber.Display("+447911123456"));
    }

    [Fact]
    public void Online_update_changes_names_and_mobile_only()
    {
        var c = Customer.Create("Jane", "Smith", "jane@example.com", "0412345678", Now);
        c.UpdateContactDetails("Jane", "Smith", "0412345678", "0498765432", "1 River Rd");
        c.UpdateComments("VIP");
        c.UpdateFromOnlineBooking("Janet", "Smyth", "0400000000");
        Assert.Equal("Janet Smyth", c.FullName);
        Assert.Equal("+61400000000", c.MobileNumber);
        Assert.Equal("+61498765432", c.SecondaryMobileNumber);
        Assert.Equal("1 River Rd", c.Address);
        Assert.Equal("VIP", c.Comments);
    }
}

public class SettingsRuleTests
{
    [Fact]
    public void Price_on_request_clears_prices()
    {
        var a = AddonDefinition.Create(new AddonDetails("Hamper", null, true, 10, 10, 10, false, null, null), 0, Now);
        Assert.Null(a.Prices);
        Assert.Null(a.PriceFor(PeriodType.Midweek));
    }

    [Fact]
    public void Capacity_unit_label_is_cleared_without_a_capacity()
    {
        var a = AddonDefinition.Create(new AddonDetails("Parking", null, true, null, null, null, true, null, "car spots"), 0, Now);
        Assert.Null(a.CapacityUnitLabel);
        a.Update(new AddonDetails("Parking", null, true, null, null, null, true, 11, "car spots"));
        Assert.Equal("car spots", a.CapacityUnitLabel);
        Assert.Equal("6 car spots remaining", a.RemainingText(6));
    }

    [Fact]
    public void Priced_addons_need_every_price()
    {
        var e = Assert.Throws<DomainValidationException>(() =>
            AddonDefinition.Create(new AddonDetails("Motor", null, false, 95, null, 130, false, null, null), 0, Now));
        Assert.True(e.Errors.ContainsKey("weekendPrice"));
    }

    [Fact]
    public void Season_ranges_may_wrap_but_must_not_overlap()
    {
        var peak = Peak();
        var e = Assert.Throws<DomainValidationException>(() =>
            Season.Create("Summer", [new SeasonRange(new DayMonth(15, 1), new DayMonth(15, 2))], [Normal(), OffPeak(), peak], Now));
        Assert.True(e.Errors.ContainsKey("ranges"));
        var ok = Season.Create("Spring", [new SeasonRange(new DayMonth(1, 9), new DayMonth(30, 11))], [Normal(), OffPeak(), peak], Now);
        Assert.True(ok.Ranges[0].Contains(D("2027-10-01")));
        Assert.True(peak.Ranges[0].Contains(D("2027-01-31")));
        Assert.Throws<ConflictException>(() => Normal().EnsureCanDelete());
    }

    [Fact]
    public void Bsb_is_six_digits_and_displayed_with_a_dash()
    {
        var s = BusinessSettings.Create(new BusinessSettingsDetails(1000, 30, D("2027-11-30"), null, null, null, null, "Berger", "123-456", "12345678"), Now);
        Assert.Equal("123456", s.Bsb);
        Assert.Equal("123-456", s.BsbDisplay);
        Assert.Throws<DomainValidationException>(() => s.Update(new BusinessSettingsDetails(1000, 30, D("2027-11-30"), null, null, null, null, null, "12345", "12345")));
    }

    [Fact]
    public void Email_templates_reject_unknown_placeholders()
    {
        var e = Assert.Throws<DomainValidationException>(() => EmailTemplate.Create(EmailTemplateKey.PaymentDue, "Hi {{CustomerName}}", "{{Nope}}", Now));
        Assert.Contains("Unknown placeholder {{Nope}}.", e.Errors["htmlBody"]);
    }
}
