using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Customers;

public class CustomerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Email_is_normalised_and_the_address_starts_empty()
    {
        var c = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("Jane@Example.com "), MobileNumber.Create("0412345678"), Now);
        Assert.Equal("jane@example.com", c.Email.Value);
        Assert.Null(c.Address);
        Assert.Equal("Jane Smith", c.FullName);
    }

    [Fact]
    public void Booking_again_updates_names_and_mobile_but_not_address_or_comments()
    {
        var c = Customer.Create(CustomerId.New(), "Jane", "Smith", EmailAddress.Create("jane@example.com"), null, Now);
        c.UpdateByStaff("Jane", "Smith", c.Email, null,
            Address.Create("1 River Rd", null, "Brooklyn", "NSW", "2083", "AU"), "Likes Misty Blue", Now);
        c.UpdateFromBooking("Janet", "Smith-Jones", MobileNumber.Create("0412345678"), Now);
        Assert.Equal("Janet", c.FirstName);
        Assert.Equal("+61412345678", c.MobileNumber!.Value);
        Assert.Equal("1 River Rd", c.Address!.Line1);
        Assert.Equal("Likes Misty Blue", c.Comments);
    }
}
