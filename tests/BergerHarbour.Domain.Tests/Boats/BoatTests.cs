using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Boats;

public class BoatTests
{
    private static readonly Season Normal = Season.CreateDefault(new SeasonId("normal"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static BoatDetails Details(
        int berth = 12, int? adultBeds = 6, int? kidsBeds = 2, string? bedding = "4 queens", string? imageUrl = null, bool rates = true,
        bool spa = false, bool pets = false) =>
        new("Pacific Blue", "pacific-blue", BoatType.House, berth, adultBeds, kidsBeds, bedding, 2000, null, imageUrl, spa, pets,
            rates ? PeriodTypes.Online.Select(t => new BoatRate(Normal.Id, t, 4060)).ToList() : [], []);

    [Fact]
    public void Activation_needs_beds_bedding_and_every_online_rate_but_not_image_spa_or_pets()
    {
        var boat = Boat.Create(BoatId.New(), Details(imageUrl: null, spa: false, pets: false), Now);
        boat.Activate([Normal], Now);
        Assert.True(boat.IsActive);
    }

    [Fact]
    public void Activation_reports_what_is_missing()
    {
        var boat = Boat.Create(BoatId.New(), Details(adultBeds: null, kidsBeds: null, bedding: null, rates: false), Now);
        var e = Assert.Throws<DomainValidationException>(() => boat.Activate([Normal], Now));
        Assert.True(e.Errors.Has("isActive"));
        Assert.Contains("Number of adult beds", boat.MissingForActivation([Normal]));
        Assert.Contains("Normal Mid-week rate", boat.MissingForActivation([Normal]));
        Assert.False(boat.IsActive);
    }

    [Fact]
    public void Zero_kids_beds_is_allowed_for_activation()
    {
        var boat = Boat.Create(BoatId.New(), Details(kidsBeds: 0), Now);
        boat.Activate([Normal], Now);
        Assert.True(boat.IsActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Berth_can_never_be_less_than_adult_plus_kids_beds_on_create_and_edit(bool active)
    {
        var e = Assert.Throws<DomainValidationException>(() => Boat.Create(BoatId.New(), Details(berth: 7), Now));
        Assert.Equal(["Berth can't be less than adult beds + kids beds (8)."], e.Errors.ByField["berth"]);
        var boat = Boat.Create(BoatId.New(), Details(), Now);
        if (active)
        {
            boat.Activate([Normal], Now);
        }

        Assert.Throws<DomainValidationException>(() => boat.Update(Details(berth: 7), [Normal], Now));
        Assert.Equal(12, boat.Berth);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://bergerhouseboats.com.au/wp-content/uploads/2025/03/1-12.jpg", true)]
    [InlineData("https://www.bergerhouseboats.com.au/a.jpg", true)]
    [InlineData("http://bergerhouseboats.com.au/a.jpg", false)]
    [InlineData("https://example.com/a.jpg", false)]
    [InlineData("https://bergerhouseboats.com.au.evil.com/a.jpg", false)]
    [InlineData("/wp-content/a.jpg", false)]
    public void Image_url_must_be_https_on_the_wordpress_site(string? url, bool valid) => Assert.Equal(valid, Boat.IsValidImageUrl(url));

    [Fact]
    public void Rooming_warning_when_guests_exceed_adult_plus_kids_beds()
    {
        var boat = Boat.Create(BoatId.New(), Details(), Now);
        Assert.Null(boat.RoomingWarningFor(8));
        Assert.Equal(
            "This boat has 6 adult beds and 2 children's beds (4 queens). With 9 guests, some guests will need to share beds. " +
            "Please make sure your group's sleeping arrangements suit this layout.",
            boat.RoomingWarningFor(9));
    }

    [Fact]
    public void Effective_deposit_falls_back_to_the_settings_default()
    {
        var settings = BusinessSettings.Create(1000, 30, new DateOnly(2027, 11, 30), null, null, null, null, null, null, null);
        Assert.Equal(1000m, Boat.Create(BoatId.New(), Details(), Now).EffectiveDeposit(settings));
        var own = Boat.Create(BoatId.New(), Details() with { DepositAmount = 250 }, Now);
        Assert.Equal(250m, own.EffectiveDeposit(settings));
    }

    [Fact]
    public void Boats_are_listed_by_berth_descending_then_name()
    {
        var a = Boat.Create(BoatId.New(), Details() with { Name = "Ocean Spirit" }, Now);
        var b = Boat.Create(BoatId.New(), Details() with { Name = "Kalinda" }, Now);
        var c = Boat.Create(BoatId.New(), Details(berth: 2, adultBeds: 1, kidsBeds: 0) with { Name = "Runaway Bear" }, Now);
        Assert.Equal(["Kalinda", "Ocean Spirit", "Runaway Bear"], Boat.InListOrder([c, a, b]).Select(x => x.Name));
    }
}
