using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Boats;

public class BoatImageTests
{
    [Theory]
    [InlineData("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", true)]
    [InlineData("https://www.bergerhouseboats.com.au/a.jpg", true)]
    [InlineData("  https://bergerhouseboats.com.au/a.jpg  ", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("http://bergerhouseboats.com.au/a.jpg", false)]
    [InlineData("https://example.com/a.jpg", false)]
    [InlineData("https://bergerhouseboats.com.au.evil.com/a.jpg", false)]
    [InlineData("/wp-content/a.jpg", false)]
    public void Url_must_be_https_on_the_wordpress_site(string? url, bool valid) => Assert.Equal(valid, BoatImage.IsValidUrl(url));

    [Fact]
    public void Url_and_description_are_trimmed_and_a_blank_description_is_none()
    {
        var image = BoatImage.Create("  https://bergerhouseboats.com.au/a.jpg ", "  Lounge  ");
        Assert.Equal("https://bergerhouseboats.com.au/a.jpg", image.Url);
        Assert.Equal("Lounge", image.Description);
        Assert.Null(BoatImage.Create("https://bergerhouseboats.com.au/a.jpg", "   ").Description);
    }

    [Fact]
    public void Errors_are_on_the_image_concerned()
    {
        var e = Assert.Throws<DomainValidationException>(() => BoatImage.Create("https://example.com/a.jpg", new string('x', 121), "images[3]"));
        Assert.Equal(["Use an https:// link to an image on bergerhouseboats.com.au."], e.Errors.ByField["images[3].url"]);
        Assert.Equal(["Keep the description to 120 characters."], e.Errors.ByField["images[3].description"]);
        Assert.Equal(new string('x', 120), BoatImage.Create("https://bergerhouseboats.com.au/a.jpg", new string('x', 120)).Description);
    }
}
