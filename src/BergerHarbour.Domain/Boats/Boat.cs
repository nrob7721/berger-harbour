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

    private Boat(BoatId id, int version, string name, string slug, BoatType type, int maxNoOfGuests, int? noOfBeds,
        string? beddingDescription, decimal securityBond, IEnumerable<BoatRate> rates, IEnumerable<AddonId> allowedAddonIds,
        bool isActive, DateTimeOffset createdDate, DateTimeOffset modifiedDate)
        : base(id, version)
    {
        Name = name;
        Slug = slug;
        Type = type;
        MaxNoOfGuests = maxNoOfGuests;
        NoOfBeds = noOfBeds;
        BeddingDescription = beddingDescription;
        SecurityBond = securityBond;
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

    /// <summary>Physical beds; a double or queen counts as one bed. Drives the rooming warning.</summary>
    public int? NoOfBeds { get; private set; }

    public string? BeddingDescription { get; private set; }

    /// <summary>Displayed only; the bond is not taken or held by the system.</summary>
    public decimal SecurityBond { get; private set; }

    public IReadOnlyList<BoatRate> Rates => _rates;

    public IReadOnlyList<AddonId> AllowedAddonIds => _allowedAddonIds;

    /// <summary>Inactive boats cannot be booked online or by staff. Boats are never deleted.</summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    /// <summary>Creates an inactive boat. It must be completed (beds, bedding, rates) before it can be activated.</summary>
    public static Boat Create(string name, string slug, BoatType type, int maxNoOfGuests, DateTimeOffset now) =>
        CreateWithId(BoatId.New(), name, slug, type, maxNoOfGuests, now);

    public static Boat CreateWithId(BoatId id, string name, string slug, BoatType type, int maxNoOfGuests, DateTimeOffset now) =>
        new(id, 0, Guard.Required(name, "name", "Name", 100), ValidateSlug(slug), type, ValidateMaxGuests(maxNoOfGuests),
            null, null, 0m, [], [], false, now, now);

    public void UpdateDetails(string name, string slug, int maxNoOfGuests, int? noOfBeds, string? beddingDescription,
        decimal securityBond, DateTimeOffset now)
    {
        if (noOfBeds is < 1)
        {
            throw new DomainValidationException("noOfBeds", "Number of beds must be at least 1.");
        }

        Name = Guard.Required(name, "name", "Name", 100);
        Slug = ValidateSlug(slug);
        MaxNoOfGuests = ValidateMaxGuests(maxNoOfGuests);
        NoOfBeds = noOfBeds;
        BeddingDescription = Guard.Optional(beddingDescription, "beddingDescription", "Bedding description", 500);
        SecurityBond = Guard.Money(securityBond, "securityBond", "Security bond");
        ModifiedDate = now;
    }

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
    /// What is missing before the boat can be activated: beds, bedding description and a rate for every season ×
    /// {Midweek, Weekend, Week}. Empty when the boat is complete.
    /// </summary>
    public IReadOnlyList<string> MissingActivationData(IEnumerable<Season> seasons)
    {
        var missing = new List<string>();
        if (NoOfBeds is null)
        {
            missing.Add("Number of beds");
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

    /// <summary>True when an online booking for this many guests must show and accept the rooming warning.</summary>
    public bool RequiresRoomingWarning(int numberOfGuests) => NoOfBeds is { } beds && numberOfGuests > beds;

    /// <summary>The exact rooming warning text; the customer's acceptance must match it.</summary>
    public string RoomingWarningText(int numberOfGuests) =>
        $"This boat has {NoOfBeds} beds ({BeddingDescription}). With {numberOfGuests} guests, some guests will need to " +
        "share beds. Please make sure your group's sleeping arrangements suit this layout.";

    public BoatSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, Slug, Type, MaxNoOfGuests, NoOfBeds, BeddingDescription, SecurityBond,
            _rates.Select(r => new BoatRateSnapshot(r.SeasonId.Value, r.PeriodType, r.Price)).ToList(),
            _allowedAddonIds.Select(a => a.Value).ToList(), IsActive, CreatedDate, ModifiedDate);

    public static Boat FromSnapshot(BoatSnapshot s) =>
        new(new BoatId(s.Id), s.Version, s.Name, s.Slug, s.Type, s.MaxNoOfGuests, s.NoOfBeds, s.BeddingDescription,
            s.SecurityBond, s.Rates.Select(r => new BoatRate(new SeasonId(r.SeasonId), r.PeriodType, r.Price)),
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
    int? NoOfBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    IReadOnlyList<BoatRateSnapshot> Rates,
    IReadOnlyList<string> AllowedAddonIds,
    bool IsActive,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);
