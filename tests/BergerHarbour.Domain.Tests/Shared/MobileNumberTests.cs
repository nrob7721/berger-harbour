using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Shared;

public class MobileNumberTests
{
    [Fact]
    public void Australian_mobile_is_accepted_and_stored_in_e164()
    {
        Assert.Equal("+61412345678", MobileNumber.Create("0412 345 678").Value);
        Assert.Equal("+61412345678", MobileNumber.Create("+61 412 345 678").Value);
    }

    [Fact]
    public void Australian_landline_is_rejected()
    {
        var e = Assert.Throws<DomainValidationException>(() => MobileNumber.Create("+61295551234"));
        Assert.Equal(MobileNumber.Error, e.Message);
    }

    [Fact]
    public void Fixed_line_or_mobile_numbers_are_accepted() => Assert.Equal("+12015550123", MobileNumber.Create("+1 201-555-0123").Value);

    [Fact]
    public void Shown_national_for_australia_and_international_otherwise()
    {
        Assert.Equal("0412 345 678", MobileNumber.Create("+61412345678").Display);
        Assert.StartsWith("+44", MobileNumber.Create("+447400123456").Display);
    }

    [Fact]
    public void Garbage_is_rejected() => Assert.Throws<DomainValidationException>(() => MobileNumber.Create("call me"));
}
