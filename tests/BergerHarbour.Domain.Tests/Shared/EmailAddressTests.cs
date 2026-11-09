using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Shared;

public class EmailAddressTests
{
    [Theory]
    [InlineData("jane@gmail.com")]
    [InlineData("jane@bigpond.net.au")]
    [InlineData("jane@example.co.uk")]
    [InlineData(" Jane.Smith+tag@Example.COM ")]
    public void Accepted(string email) => Assert.True(EmailAddress.IsValid(email));

    [Theory]
    [InlineData("jane@gmail")]
    [InlineData("jane@b.c")]
    [InlineData("jane@gmail..com")]
    [InlineData("jane@-gmail.com")]
    [InlineData("jane@gmail.c0m")]
    [InlineData("jane smith@gmail.com")]
    [InlineData("jane@@gmail.com")]
    [InlineData("@gmail.com")]
    public void Rejected(string email)
    {
        Assert.False(EmailAddress.IsValid(email));
        var e = Assert.Throws<DomainValidationException>(() => EmailAddress.Create(email));
        Assert.Equal(EmailAddress.Error, e.Message);
    }

    [Fact]
    public void Stored_trimmed_and_lower_case() => Assert.Equal("jane.smith@example.com", EmailAddress.Create("  Jane.Smith@Example.COM ").Value);

    [Fact]
    public void At_most_254_characters() => Assert.False(EmailAddress.IsValid(new string('a', 245) + "@gmail.com"));
}
