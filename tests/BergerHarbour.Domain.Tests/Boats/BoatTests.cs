using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests.Boats;

public class BoatTests
{
    private static readonly Season Normal = Season.CreateDefault(new SeasonId("normal"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static BoatDetails Details(
        int berth = 12, int? adultBeds = 6, int? kidsBeds = 2, string? bedding = "4 queens", IReadOnlyList<BoatImage>? images = null,
        IReadOnlyList<string>? tags = null, bool rates = true, bool spa = false, bool pets = false) =>
        new("Pacific Blue", "pacific-blue", BoatType.House, berth, adultBeds, kidsBeds, bedding, 2000, null, images ?? [], tags ?? [], spa, pets,
            rates ? PeriodTypes.Online.Select(t => new BoatRate(Normal.Id, t, 4060)).ToList() : [], []);

    private static BoatImage Photo(int i, string? description = null) =>
        BoatImage.Create($"https://bergerhouseboats.com.au/wp-content/uploads/{i}.jpg", description);

    [Fact]
    public void Activation_needs_beds_bedding_and_every_online_rate_but_not_images_tags_spa_or_pets()
    {
        var boat = Boat.Create(BoatId.New(), Details(images: [], tags: [], spa: false, pets: false), Now);
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

    [Fact]
    public void Images_keep_their_order_and_the_first_is_the_thumbnail_image()
    {
        var boat = Boat.Create(BoatId.New(), Details(images: [Photo(3), Photo(1, "Lounge"), Photo(2)]), Now);
        Assert.Equal(["3", "1", "2"], boat.Images.Select(i => Path.GetFileNameWithoutExtension(i.Url)));
        Assert.Equal("https://bergerhouseboats.com.au/wp-content/uploads/3.jpg", boat.ThumbnailUrl);
        Assert.Equal("Lounge", boat.Images[1].Description);
        Assert.Null(Boat.Create(BoatId.New(), Details(), Now).ThumbnailUrl);
    }

    [Fact]
    public void At_most_30_images_on_create_and_edit()
    {
        Boat.Create(BoatId.New(), Details(images: Enumerable.Range(1, 30).Select(i => Photo(i)).ToList()), Now);
        var tooMany = Details(images: Enumerable.Range(1, 31).Select(i => Photo(i)).ToList());
        var e = Assert.Throws<DomainValidationException>(() => Boat.Create(BoatId.New(), tooMany, Now));
        Assert.Equal(["A boat can have at most 30 photos."], e.Errors.ByField["images"]);
        var boat = Boat.Create(BoatId.New(), Details(), Now);
        Assert.Throws<DomainValidationException>(() => boat.Update(tooMany, [Normal], Now));
        Assert.Empty(boat.Images);
    }

    [Fact]
    public void Tags_are_trimmed_and_keep_their_order()
    {
        var boat = Boat.Create(BoatId.New(), Details(tags: ["  Sleeps 12 ", "Heated spa", "Air conditioning"]), Now);
        Assert.Equal(["Sleeps 12", "Heated spa", "Air conditioning"], boat.Tags);
    }

    [Theory]
    [InlineData("   ", "A tag can't be blank.")]
    [InlineData("1234567890123456789012345678901234567890123456789012345678901", "Keep each tag to 60 characters.")]
    [InlineData("HEATED SPA", "This tag is already on the boat.")]
    public void Blank_long_and_duplicate_tags_are_rejected_on_the_tag(string tag, string message)
    {
        var e = Assert.Throws<DomainValidationException>(() => Boat.Create(BoatId.New(), Details(tags: ["Sleeps 12", "Heated spa", tag]), Now));
        Assert.Equal([message], e.Errors.ByField["tags[2]"]);
    }

    [Fact]
    public void A_60_character_tag_is_allowed_and_at_most_30_tags()
    {
        Boat.Create(BoatId.New(), Details(tags: [new string('x', 60)]), Now);
        Boat.Create(BoatId.New(), Details(tags: Enumerable.Range(1, 30).Select(i => $"Tag {i}").ToList()), Now);
        var e = Assert.Throws<DomainValidationException>(() =>
            Boat.Create(BoatId.New(), Details(tags: Enumerable.Range(1, 31).Select(i => $"Tag {i}").ToList()), Now));
        Assert.Equal(["A boat can have at most 30 tags."], e.Errors.ByField["tags"]);
    }

    [Fact]
    public void Tag_rules_apply_on_edit()
    {
        var boat = Boat.Create(BoatId.New(), Details(tags: ["Spa"]), Now);
        var e = Assert.Throws<DomainValidationException>(() => boat.Update(Details(tags: ["Spa", "spa"]), [Normal], Now));
        Assert.True(e.Errors.Has("tags[1]"));
        Assert.Equal(["Spa"], boat.Tags);
    }

    [Fact]
    public void The_seed_backfill_sets_photos_and_tags_only_when_the_boat_has_none()
    {
        var bare = Boat.Create(BoatId.New(), Details(), Now);
        Assert.True(bare.FillMissingPhotosAndTags([Photo(1)], ["Sleeps 12"], Now.AddDays(1)));
        Assert.Equal([Photo(1)], bare.Images);
        Assert.Equal(["Sleeps 12"], bare.Tags);
        Assert.Equal(Now.AddDays(1), bare.ModifiedDate);

        var edited = Boat.Create(BoatId.New(), Details(images: [Photo(9)], tags: ["Mine"]), Now);
        Assert.False(edited.FillMissingPhotosAndTags([Photo(1)], ["Sleeps 12"], Now.AddDays(1)));
        Assert.Equal([Photo(9)], edited.Images);
        Assert.Equal(["Mine"], edited.Tags);
        Assert.Equal(Now, edited.ModifiedDate);

        var tagsOnly = Boat.Create(BoatId.New(), Details(tags: ["Mine"]), Now);
        Assert.True(tagsOnly.FillMissingPhotosAndTags([Photo(1)], ["Sleeps 12"], Now));
        Assert.Equal([Photo(1)], tagsOnly.Images);
        Assert.Equal(["Mine"], tagsOnly.Tags);
    }

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
    public void Boats_are_listed_by_normal_season_week_rate_descending_then_name_and_boats_without_it_last()
    {
        static List<BoatRate> Week(decimal price, SeasonId? season = null) => [new(season ?? Normal.Id, PeriodType.Week, price)];
        var peak = new SeasonId("peak");
        var a = Boat.Create(BoatId.New(), Details() with { Name = "Ocean Spirit", Rates = Week(5000) }, Now);
        var b = Boat.Create(BoatId.New(), Details() with { Name = "Kalinda", Rates = Week(5000) }, Now);
        var c = Boat.Create(BoatId.New(), Details(berth: 2, adultBeds: 1, kidsBeds: 0) with { Name = "Runaway Bear", Rates = Week(9000) }, Now);
        var d = Boat.Create(BoatId.New(), Details() with { Name = "Bluebird", Rates = Week(9999, peak) }, Now);
        var e = Boat.Create(BoatId.New(), Details() with { Name = "Albatross", Rates = [] }, Now);
        Assert.Equal(
            ["Runaway Bear", "Kalinda", "Ocean Spirit", "Albatross", "Bluebird"],
            Boat.InListOrder([e, d, c, a, b], [Normal, Season.Rehydrate(peak, "Peak", false, [], 1)]).Select(x => x.Name));
    }
}
