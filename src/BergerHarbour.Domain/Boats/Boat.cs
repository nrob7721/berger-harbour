using System.Text.RegularExpressions;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

/// <summary>A boat in the fleet. Boats are never deleted; inactive boats can't be booked but keep their history.</summary>
public sealed partial class Boat : AggregateRoot
{
    private Boat(BoatId id, DateTimeOffset createdDate)
    {
        Id = id;
        Name = string.Empty;
        Slug = string.Empty;
        Rates = [];
        AllowedAddonIds = [];
        CreatedDate = createdDate;
        ModifiedDate = createdDate;
    }

    public BoatId Id { get; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public BoatType Type { get; private set; }

    public int Berth { get; private set; }

    public int? AdultBeds { get; private set; }

    public int? KidsBeds { get; private set; }

    public string? BeddingDescription { get; private set; }

    public decimal SecurityBond { get; private set; }

    public decimal? DepositAmount { get; private set; }

    public string? ImageUrl { get; private set; }

    public bool HasSpa { get; private set; }

    public bool IsPetFriendly { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<BoatRate> Rates { get; private set; }

    public IReadOnlyList<AddonId> AllowedAddonIds { get; private set; }

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    /// <summary>Adult beds + kids beds; drives the rooming warning.</summary>
    public int TotalBeds => (AdultBeds ?? 0) + (KidsBeds ?? 0);

    /// <summary>Sorting used everywhere boats are listed: berth descending, then name.</summary>
    public static IOrderedEnumerable<Boat> InListOrder(IEnumerable<Boat> boats) =>
        boats.OrderByDescending(b => b.Berth).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>A new boat, inactive until <see cref="Activate"/> succeeds.</summary>
    public static Boat Create(BoatId id, BoatDetails details, DateTimeOffset now)
    {
        var boat = new Boat(id, now) { Type = details.Type };
        boat.Apply(details, now);
        return boat;
    }

    public static Boat Rehydrate(
        BoatId id, BoatDetails details, bool isActive, DateTimeOffset createdDate, DateTimeOffset modifiedDate, int version)
    {
        var boat = new Boat(id, createdDate)
        {
            Name = details.Name ?? string.Empty,
            Slug = details.Slug ?? string.Empty,
            Type = details.Type,
            Berth = details.Berth,
            AdultBeds = details.AdultBeds,
            KidsBeds = details.KidsBeds,
            BeddingDescription = details.BeddingDescription,
            SecurityBond = details.SecurityBond,
            DepositAmount = details.DepositAmount,
            ImageUrl = details.ImageUrl,
            HasSpa = details.HasSpa,
            IsPetFriendly = details.IsPetFriendly,
            Rates = details.Rates,
            AllowedAddonIds = details.AllowedAddonIds,
            IsActive = isActive,
            ModifiedDate = modifiedDate,
        };
        boat.SetPersistedVersion(version);
        return boat;
    }

    /// <summary>
    /// Empty, or an absolute https:// URL on bergerhouseboats.com.au or www.bergerhouseboats.com.au.
    /// </summary>
    public static bool IsValidImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.ToLowerInvariant() is "bergerhouseboats.com.au" or "www.bergerhouseboats.com.au";
    }

    /// <summary>Edits every field; an active boat must still meet the activation rule.</summary>
    public void Update(BoatDetails details, IReadOnlyList<Season> seasons, DateTimeOffset now)
    {
        Apply(details, now);
        if (IsActive)
        {
            EnsureCanBeActive(seasons);
        }
    }

    /// <summary>
    /// Requires adult beds, kids beds (0 allowed), a bedding description and a rate for every season × {Midweek, Weekend, Week}.
    /// </summary>
    public void Activate(IReadOnlyList<Season> seasons, DateTimeOffset now)
    {
        EnsureCanBeActive(seasons);
        if (!IsActive)
        {
            IsActive = true;
            ModifiedDate = now;
        }
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            IsActive = false;
            ModifiedDate = now;
        }
    }

    /// <summary>What is missing before the boat can be activated (empty when it can be).</summary>
    public IReadOnlyList<string> MissingForActivation(IReadOnlyList<Season> seasons)
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
            foreach (var type in PeriodTypes.Online)
            {
                if (RateFor(season.Id, type) is null)
                {
                    missing.Add($"{season.Name} {Label(type)} rate");
                }
            }
        }

        return missing;
    }

    public decimal? RateFor(SeasonId seasonId, PeriodType periodType) =>
        Rates.FirstOrDefault(r => r.SeasonId == seasonId && r.PeriodType == periodType)?.Price;

    /// <summary>The boat's own deposit, or the Settings default.</summary>
    public decimal EffectiveDeposit(BusinessSettings settings) => DepositAmount ?? settings.DepositAmount;

    public bool AllowsAddon(AddonId id) => AllowedAddonIds.Contains(id);

    /// <summary>The rooming warning shown when guests exceed adult beds + kids beds, or null.</summary>
    public string? RoomingWarningFor(int numberOfGuests) =>
        numberOfGuests > TotalBeds
            ? $"This boat has {AdultBeds ?? 0} adult beds and {KidsBeds ?? 0} children's beds ({BeddingDescription ?? string.Empty}). " +
              $"With {numberOfGuests} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout."
            : null;

    private static string Label(PeriodType type) => type switch
    {
        PeriodType.Midweek => "Mid-week",
        PeriodType.LongWeekend => "Long weekend",
        _ => type.ToString(),
    };

    private void EnsureCanBeActive(IReadOnlyList<Season> seasons)
    {
        var missing = MissingForActivation(seasons);
        if (missing.Count > 0)
        {
            throw new DomainValidationException("isActive", $"Can't activate this boat. Missing: {string.Join(", ", missing)}.");
        }
    }

    private void Apply(BoatDetails details, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        var name = (details.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add("name", "Name is required.");
        }
        else if (name.Length > 80)
        {
            errors.Add("name", "Name must be 80 characters or fewer.");
        }

        var slug = (details.Slug ?? string.Empty).Trim();
        if (slug.Length == 0)
        {
            errors.Add("slug", "Slug is required.");
        }
        else if (slug.Length > 80 || !SlugPattern().IsMatch(slug))
        {
            errors.Add("slug", "Use lower-case letters, numbers and hyphens, e.g. pacific-blue.");
        }

        if (details.Berth < 1)
        {
            errors.Add("berth", "Berth must be at least 1.");
        }

        if (details.AdultBeds < 0)
        {
            errors.Add("adultBeds", "Adult beds can't be negative.");
        }

        if (details.KidsBeds < 0)
        {
            errors.Add("kidsBeds", "Kids beds can't be negative.");
        }

        var beds = (details.AdultBeds ?? 0) + (details.KidsBeds ?? 0);
        if (details.Berth >= 1 && details.Berth < beds)
        {
            errors.Add("berth", $"Berth can't be less than adult beds + kids beds ({beds}).");
        }

        var bedding = string.IsNullOrWhiteSpace(details.BeddingDescription) ? null : details.BeddingDescription.Trim();
        if (bedding?.Length > 500)
        {
            errors.Add("beddingDescription", "Bedding description must be 500 characters or fewer.");
        }

        if (details.SecurityBond < 0)
        {
            errors.Add("securityBond", "Security bond can't be negative.");
        }

        if (details.DepositAmount <= 0)
        {
            errors.Add("depositAmount", "Deposit must be more than $0, or left empty to use the default.");
        }

        var image = string.IsNullOrWhiteSpace(details.ImageUrl) ? null : details.ImageUrl.Trim();
        if (!IsValidImageUrl(image))
        {
            errors.Add("imageUrl", "Use an https:// link to an image on bergerhouseboats.com.au.");
        }

        foreach (var rate in details.Rates)
        {
            if (rate.Price < 0)
            {
                errors.Add("rates", "Rates can't be negative.");
            }

            if (!PeriodTypes.Rated.Contains(rate.PeriodType))
            {
                errors.Add("rates", "Rates are only for Mid-week, Weekend, Week and Long weekend.");
            }
        }

        if (details.Rates.GroupBy(r => (r.SeasonId, r.PeriodType)).Any(g => g.Count() > 1))
        {
            errors.Add("rates", "Each season and period type can only have one rate.");
        }

        errors.ThrowIfAny();
        Name = name;
        Slug = slug;
        Berth = details.Berth;
        AdultBeds = details.AdultBeds;
        KidsBeds = details.KidsBeds;
        BeddingDescription = bedding;
        SecurityBond = details.SecurityBond;
        DepositAmount = details.DepositAmount;
        ImageUrl = image;
        HasSpa = details.HasSpa;
        IsPetFriendly = details.IsPetFriendly;
        Rates = details.Rates.ToList();
        AllowedAddonIds = details.AllowedAddonIds.Distinct().ToList();
        ModifiedDate = now;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
