using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Customers;

public class AddressTests
{
    [Fact]
    public void Parts_are_trimmed_and_blank_parts_stored_empty()
    {
        var a = Address.Create("  1 River Rd ", "   ", " Brooklyn ", " nsw ", " 2083 ", " au ")!;
        Assert.Equal("1 River Rd", a.Line1);
        Assert.Null(a.Line2);
        Assert.Equal("Brooklyn", a.Locality);
        Assert.Equal("NSW", a.State);
        Assert.Equal("2083", a.Postcode);
        Assert.Equal("AU", a.Country);
    }

    [Fact]
    public void A_partial_address_is_accepted()
    {
        var a = Address.Create(null, null, "Brooklyn", null, null, "AU")!;
        Assert.Equal("Brooklyn", a.Locality);
        Assert.Null(a.Line1);
        Assert.Null(a.Postcode);
        Assert.Equal("1 River Rd", Address.Create("1 River Rd", null, null, null, null, null)!.Line1);
    }

    [Fact]
    public void An_address_with_only_a_country_or_nothing_is_no_address()
    {
        Assert.Null(Address.Create(null, null, null, null, null, "AU"));
        Assert.Null(Address.Create(" ", "", null, "  ", null, "NZ"));
        Assert.Null(Address.Create(null, null, null, null, null, null));
    }

    [Theory]
    [InlineData("line1", 100)]
    [InlineData("line2", 100)]
    [InlineData("locality", 60)]
    [InlineData("state", 60)]
    [InlineData("postcode", 10)]
    public void Each_part_has_a_length_limit(string part, int max)
    {
        Address? Make(string value) => Address.Create(
            part == "line1" ? value : null, part == "line2" ? value : null, part == "locality" ? value : null, part == "state" ? value : null,
            part == "postcode" ? value : null, "NZ");

        Assert.NotNull(Make(new string('a', max)));
        var e = Assert.Throws<DomainValidationException>(() => Make(new string('a', max + 1)));
        Assert.True(e.Errors.Has($"address.{part}"));
    }

    [Theory]
    [InlineData("NSW")]
    [InlineData("VIC")]
    [InlineData("QLD")]
    [InlineData("SA")]
    [InlineData("WA")]
    [InlineData("TAS")]
    [InlineData("NT")]
    [InlineData("ACT")]
    public void Australian_states_and_territories_are_accepted(string state) =>
        Assert.Equal(state, Address.Create(null, null, null, state, null, "AU")!.State);

    [Fact]
    public void An_Australian_address_needs_a_listed_state_and_a_4_digit_postcode()
    {
        var e = Assert.Throws<DomainValidationException>(() => Address.Create("1 River Rd", null, null, "New South Wales", "208", "AU"));
        Assert.Equal("Choose an Australian state or territory.", e.Errors.ByField["address.state"][0]);
        Assert.Equal("Enter a 4-digit postcode.", e.Errors.ByField["address.postcode"][0]);
        Assert.Throws<DomainValidationException>(() => Address.Create(null, null, null, null, "20A3", "AU"));
    }

    [Fact]
    public void Other_countries_take_free_text_state_and_postcode()
    {
        var a = Address.Create("10 Downing St", null, "London", "Greater London", "SW1A 2AA", "GB")!;
        Assert.Equal("Greater London", a.State);
        Assert.Equal("SW1A 2AA", a.Postcode);
        Assert.Equal("New South Wales", Address.Create(null, null, null, "New South Wales", "208", null)!.State);
    }

    [Fact]
    public void The_country_must_be_an_ISO_code_from_the_list()
    {
        var e = Assert.Throws<DomainValidationException>(() => Address.Create("1 River Rd", null, null, null, null, "XK"));
        Assert.Equal("Choose a country from the list.", e.Errors.ByField["address.country"][0]);
        Assert.Throws<DomainValidationException>(() => Address.Create("1 River Rd", null, null, null, null, "Australia"));
        Assert.Equal(249, IsoCountryCodes.All.Count);
    }
}
