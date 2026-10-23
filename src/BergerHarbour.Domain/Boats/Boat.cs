using System.Text.RegularExpressions;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

public enum BoatType
{
    House,

    /// <summary>Future use only; BBQ boats are booked through a third-party system.</summary>
    BBQ,
}

public sealed record BoatRate
{
    public BoatRate(SeasonId seasonId, PeriodType periodType, decimal price)
    {
        if (!PeriodTypes.Rated.Contains(periodType))
        {
            throw new DomainValidationException("rates", $"Boats have no rate for {periodType} stays.");
        }

        SeasonId = seasonId;
        PeriodType = periodType;
        Price = Guard.PositiveMoney(price, "rates", "Rate");
    }

    public SeasonId SeasonId { get; }

    public PeriodType PeriodType { get; }

    public decimal Price { get; }
}

public sealed partial class Boat : AggregateRoot<BoatId>
{
    private List<BoatRate> _rates;
    private List<AddonId> _allowedAddonIds;

    private Boat(BoatId id, int version, string name, string slug, BoatType type, int maxNoOfGuests, int? adultBeds,
        int? kidsBeds, string? beddingDescription, decimal securityBond, decimal? depositAmount, string? imageUrl, bool hasSpa,
        bool isPetFriendly, IEnumerable<BoatRate> rates, IEnumerable<AddonId> allowedAddonIds, bool isActive,
        DateTimeOffset createdDate, DateTimeOffset modifiedDate)
        : base(id, version)
    {
        Name = name;
        Slug = slug;
        Type = type;
        MaxNoOfGuests = maxNoOfGuests;
        AdultBeds = adultBeds;
        KidsBeds = kidsBeds;
        BeddingDescription = beddingDescription;
        SecurityBond = securityBond;
        DepositAmount = depositAmount;
        ImageUrl = imageUrl;
        HasSpa = hasSpa;
        IsPetFriendly = isPetFriendly;
        _rates = rates.ToList();
        _allowedAddonIds = allowedAddonIds.ToList();
        IsActive = isActive;
        CreatedDate = createdDate;
        ModifiedDate = modifiedDate;
    }

    public string Name { get; private set; }

    /// <summary>Unique kebab-case identifier matching the WordPress URL segment.</summary>
    public string Slug { get; private set; }

    public BoatType Type { get; }

    public int MaxNoOfGuests { get; private set; }

    /// <summary>Physical adult beds; a double or queen counts as one bed.</summary>
    public int? AdultBeds { get; private set; }

    /// <summary>Physical kids beds (e.g. bunks); may be 0. Adult beds + kids beds drive the rooming warning.</summary>
    public int? KidsBeds { get; private set; }

    public string? BeddingDescription { get; private set; }

    /// <summary>Displayed only; the bond is not taken or held by the system.</summary>
    public decimal SecurityBond { get; private set; }

    /// <summary>This boat's own deposit; when null the Settings default applies (see <see cref="EffectiveDeposit"/>).</summary>
    public decimal? DepositAmount { get; private set; }

    /// <summary>Optional image on the WordPress site, shown on the booking app's boat cards and in Fleet.</summary>
    public string? ImageUrl { get; private set; }

    public bool HasSpa { get; private set; }

    public bool IsPetFriendly { get; private set; }

    public IReadOnlyList<BoatRate> Rates => _rates;

    public IReadOnlyList<AddonId> AllowedAddonIds => _allowedAddonIds;

    /// <summary>Inactive boats cannot be booked online or by staff. Boats are never deleted.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    /// <summary>Everywhere boats are listed: largest first (by max guests), then by name.</summary>
    public static IEnumerable<Boat> InDisplayOrder(IEnumerable<Boat> boats) =>
        boats.OrderByDescending(b => b.MaxNoOfGuests).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an inactive boat. It must be completed (beds, bedding, rates) before it can be activated.</summary>
    public static Boat Create(string name, string slug, BoatType type, int maxNoOfGuests, DateTimeOffset now) =>
        CreateWithId(BoatId.New(), name, slug, type, maxNoOfGuests, now);

    public static Boat CreateWithId(BoatId id, string name, string slug, BoatType type, int maxNoOfGuests, DateTimeOffset now) =>
        new(id, 0, Guard.Required(name, "name", "Name", 100), ValidateSlug(slug), type, ValidateMaxGuests(maxNoOfGuests),
            null, null, null, 0m, null, null, false, false, [], [], false, now, now);

    public void UpdateDetails(string name, string slug, int maxNoOfGuests, int? adultBeds, int? kidsBeds,
        string? beddingDescription, decimal securityBond, decimal? depositAmount, DateTimeOffset now)
    {
        if (adultBeds is < 1)
        {
            throw new DomainValidationException("adultBeds", "Number of adult beds must be at least 1.");
        }

        if (kidsBeds is < 0)
        {
            throw new DomainValidationException("kidsBeds", "Number of kids beds cannot be negative.");
        }

        Name = Guard.Required(name, "name", "Name", 100);
        Slug = ValidateSlug(slug);
        MaxNoOfGuests = ValidateMaxGuests(maxNoOfGuests);
        AdultBeds = adultBeds;
        KidsBeds = kidsBeds;
        BeddingDescription = Guard.Optional(beddingDescription, "beddingDescription", "Bedding description", 500);
        SecurityBond = Guard.Money(securityBond, "securityBond", "Security bond");
        DepositAmount = depositAmount is { } deposit ? Guard.PositiveMoney(deposit, "depositAmount", "Deposit") : null;
        ModifiedDate = now;
    }

    /// <summary>What customers can filter and see on the booking app's boat cards. None of it is needed to activate.</summary>
    public void UpdateFeatures(string? imageUrl, bool hasSpa, bool isPetFriendly, DateTimeOffset now)
    {
        ImageUrl = ValidateImageUrl(imageUrl);
        HasSpa = hasSpa;
        IsPetFriendly = isPetFriendly;
        ModifiedDate = now;
    }

    /// <summary>The deposit for new bookings of this boat: its own deposit, or the Settings default.</summary>
    public decimal EffectiveDeposit(decimal defaultDeposit) => DepositAmount ?? defaultDeposit;

    /// <summary>Adult beds plus kids beds, once both are entered.</summary>
    public int? TotalBeds => AdultBeds is { } adults ? adults + (KidsBeds ?? 0) : null;

    public void SetRates(IEnumerable<BoatRate> rates, DateTimeOffset now)
    {
        var list = rates.ToList();
        var duplicate = list.GroupBy(r => (r.SeasonId, r.PeriodType)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainValidationException("rates",
                $"There is more than one {duplicate.Key.PeriodType} rate for season {duplicate.Key.SeasonId}.");
        }

        _rates = list;
        ModifiedDate = now;
    }

    public void SetAllowedAddons(IEnumerable<AddonId> addonIds, DateTimeOffset now)
    {
        _allowedAddonIds = addonIds.Distinct().ToList();
        ModifiedDate = now;
    }

    public decimal? RateFor(SeasonId seasonId, PeriodType periodType) =>
        _rates.FirstOrDefault(r => r.SeasonId == seasonId && r.PeriodType == periodType)?.Price;

    public bool AllowsAddon(AddonId addonId) => _allowedAddonIds.Contains(addonId);

    /// <summary>
    /// What is missing before the boat can be activated: adult beds, kids beds (0 allowed), bedding description and a
    /// rate for every season × {Midweek, Weekend, Week}. Empty when the boat is complete. The image, spa and pet friendly
    /// flags are not required.
    /// </summary>
    public IReadOnlyList<string> MissingActivationData(IEnumerable<Season> seasons)
    {
        var missing = new List<string>();
        if (AdultBeds is null)
        {
            missing.Add("Number of adult beds");
        }

        if (KidsBeds is null)
        {
            missing.Add("Number of kids beds");
        }

        if (string.IsNullOrWhiteSpace(BeddingDescription))
        {
            missing.Add("Bedding description");
        }

        foreach (var season in seasons)
        {
            foreach (var period in PeriodTypes.RequiredForActivation)
            {
                if (RateFor(season.Id, period) is null)
                {
                    missing.Add($"{season.Name} {period} rate");
                }
            }
        }

        return missing;
    }

    public void Activate(IEnumerable<Season> seasons, DateTimeOffset now)
    {
        if (Type != BoatType.House)
        {
            throw new DomainValidationException("isActive", "Only house boats can be activated in v1.");
        }

        var missing = MissingActivationData(seasons);
        if (missing.Count > 0)
        {
            throw new DomainValidationException("isActive",
                $"The boat cannot be activated until these are entered: {string.Join(", ", missing)}.");
        }

        IsActive = true;
        ModifiedDate = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        ModifiedDate = now;
    }

    /// <summary>
    /// An active boat must stay complete. Call after editing an active boat, with the current seasons.
    /// </summary>
    public void EnsureStillComplete(IEnumerable<Season> seasons)
    {
        if (!IsActive)
        {
            return;
        }

        var missing = MissingActivationData(seasons);
        if (missing.Count > 0)
        {
            throw new DomainValidationException("rates",
                $"An active boat must have: {string.Join(", ", missing)}. Enter them or deactivate the boat.");
        }
    }

    /// <summary>
    /// True when this many guests (adults + children) must see and accept the rooming warning: more guests than
    /// adult beds + kids beds.
    /// </summary>
    public bool RequiresRoomingWarning(int numberOfGuests) => TotalBeds is { } beds && numberOfGuests > beds;

    /// <summary>The exact rooming warning text; the customer's acceptance must match it.</summary>
    public string RoomingWarningText(int numberOfGuests) =>
        $"This boat has {AdultBeds} adult beds and {KidsBeds ?? 0} children's beds ({BeddingDescription}). With " +
        $"{numberOfGuests} guests, some guests will need to share beds. Please make sure your group's sleeping " +
        "arrangements suit this layout.";

    public BoatSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, Slug, Type, MaxNoOfGuests, AdultBeds, KidsBeds, BeddingDescription, SecurityBond,
            DepositAmount, ImageUrl, HasSpa, IsPetFriendly, _rates.Select(r => new BoatRateSnapshot(r.SeasonId.Value, r.PeriodType, r.Price)).ToList(),
            _allowedAddonIds.Select(a => a.Value).ToList(), IsActive, CreatedDate, ModifiedDate);

    public static Boat FromSnapshot(BoatSnapshot s) =>
        new(new BoatId(s.Id), s.Version, s.Name, s.Slug, s.Type, s.MaxNoOfGuests, s.AdultBeds, s.KidsBeds,
            s.BeddingDescription, s.SecurityBond, s.DepositAmount, s.ImageUrl, s.HasSpa, s.IsPetFriendly,
            s.Rates.Select(r => new BoatRate(new SeasonId(r.SeasonId), r.PeriodType, r.Price)),
            s.AllowedAddonIds.Select(a => new AddonId(a)), s.IsActive, s.CreatedDate, s.ModifiedDate);

    private static string ValidateSlug(string slug)
    {
        var value = Guard.Required(slug, "slug", "Slug", 100);
        if (!SlugPattern().IsMatch(value))
        {
            throw new DomainValidationException("slug", "Slug must be lower-case kebab-case, e.g. pacific-blue.");
        }

        return value;
    }

    /// <summary>Empty, or an absolute https:// URL on bergerhouseboats.com.au (with or without www).</summary>
    private static string? ValidateImageUrl(string? imageUrl)
    {
        var value = Guard.Optional(imageUrl, "imageUrl", "Image URL", 1000);
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !ImageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
        {
            throw new DomainValidationException("imageUrl",
                "Image URL must be an https:// link to an image on bergerhouseboats.com.au.");
        }

        return value;
    }

    private static readonly string[] ImageHosts = ["bergerhouseboats.com.au", "www.bergerhouseboats.com.au"];

    private static int ValidateMaxGuests(int maxNoOfGuests) => maxNoOfGuests >= 1
        ? maxNoOfGuests
        : throw new DomainValidationException("maxNoOfGuests", "Maximum guests must be at least 1.");

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public sealed record BoatRateSnapshot(string SeasonId, PeriodType PeriodType, decimal Price);

public sealed record BoatSnapshot(
    string Id,
    int Version,
    string Name,
    string Slug,
    BoatType Type,
    int MaxNoOfGuests,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal? DepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly,
    IReadOnlyList<BoatRateSnapshot> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    bool IsActive,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);
