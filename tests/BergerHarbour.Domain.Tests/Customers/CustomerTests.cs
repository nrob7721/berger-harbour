using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Customers;

public class CustomerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Email_is_normalised_and_the_staff_only_fields_start_empty()
    {
        var c = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("Jane@Example.com "), MobileNumber.Create("0412345678"), Now);
        Assert.Equal("jane@example.com", c.Email.Value);
        Assert.Null(c.SecondaryMobileNumber);
        Assert.Null(c.Address);
        Assert.Equal("Jane Smith", c.FullName);
    }

    [Fact]
    public void Address_is_trimmed_blank_stored_empty_and_at_most_500_characters()
    {
        Assert.Equal("1 River Rd\nBrooklyn", Customer.NormaliseAddress("  1 River Rd\nBrooklyn \n"));
        Assert.Null(Customer.NormaliseAddress("   "));
        Assert.Equal(500, Customer.NormaliseAddress(new string('a', 500))!.Length);
        var e = Assert.Throws<DomainValidationException>(() => Customer.NormaliseAddress(new string('a', 501)));
        Assert.True(e.Errors.Has("address"));
    }

    [Fact]
    public void Booking_again_updates_names_and_mobile_but_not_staff_fields()
    {
        var c = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("jane@example.com"), null, Now);
        c.UpdateByStaff("Jane", "Smith", c.Email, null, MobileNumber.Create("0412000111"), "1 River Rd", "Likes Misty Blue", Now);
        c.UpdateFromBooking("Janet", "Smith-Jones", MobileNumber.Create("0412345678"), Now);
        Assert.Equal("Janet", c.FirstName);
        Assert.Equal("+61412345678", c.MobileNumber!.Value);
        Assert.Equal("+61412000111", c.SecondaryMobileNumber!.Value);
        Assert.Equal("1 River Rd", c.Address);
        Assert.Equal("Likes Misty Blue", c.Comments);
    }
}
