using System.Text.RegularExpressions;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Boats;

public enum BoatType { House, BBQ }

public sealed record BoatRate(string SeasonId, PeriodType PeriodType, decimal Price);

public sealed record BoatDetails(
    string Name,
    string Slug,
    BoatType Type,
    int Berth,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal? DepositAmount,
    string? ImageUrl,
    bool HasSpa,
    bool IsPetFriendly);

public sealed partial class Boat : AggregateRoot
{
    /// <summary>Period types that need a rate in every season before a boat can be activated.</summary>
    public static readonly PeriodType[] RequiredRatePeriods = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week];
    /// <summary>Period types that can have a rate.</summary>
    public static readonly PeriodType[] RatedPeriods = [PeriodType.Midweek, PeriodType.Weekend, PeriodType.Week, PeriodType.LongWeekend];

    public BoatId Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public BoatType Type { get; private set; }
    public int Berth { get; private set; }
    public int? AdultBeds { get; private set; }
    public int? KidsBeds { get; private set; }
    public string? BeddingDescription { get; private set; }
    public decimal SecurityBond { get; private set; }
    public decimal? DepositAmount { get; private set; }
    public IReadOnlyList<BoatRate> Rates { get; private set; } = [];
    public IReadOnlyList<string> AllowedAddonIds { get; private set; } = [];
    public string? ImageUrl { get; private set; }
    public bool HasSpa { get; private set; }
    public bool IsPetFriendly { get; private set; }
    public bool IsActive { get; private set; }

    public int TotalBeds => (AdultBeds ?? 0) + (KidsBeds ?? 0);

    private Boat() { }

    public static Boat Create(BoatDetails details, DateTimeOffset now)
    {
        var b = new Boat { Id = BoatId.New() };
        b.UpdateDetails(details);
        b.InitialiseAudit(now);
        return b;
    }

    public static Boat Rehydrate(BoatId id, BoatDetails d, IEnumerable<BoatRate> rates, IEnumerable<string> allowedAddonIds, bool isActive,
        int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var b = new Boat
        {
            Id = id, Name = d.Name, Slug = d.Slug, Type = d.Type, Berth = d.Berth, AdultBeds = d.AdultBeds, KidsBeds = d.KidsBeds,
            BeddingDescription = d.BeddingDescription, SecurityBond = d.SecurityBond, DepositAmount = d.DepositAmount, ImageUrl = d.ImageUrl,
            HasSpa = d.HasSpa, IsPetFriendly = d.IsPetFriendly, Rates = rates.ToList(), AllowedAddonIds = allowedAddonIds.ToList(), IsActive = isActive,
        };
        b.RestoreAudit(version, created, modified);
        return b;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public static bool IsValidImageUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host is "bergerhouseboats.com.au" or "www.bergerhouseboats.com.au");

    public void UpdateDetails(BoatDetails d)
    {
        var errors = new ValidationErrors();
        var name = errors.Capture(() => Text.Required(d.Name, "name", "Name", 80));
        var slug = (d.Slug ?? "").Trim();
        if (slug.Length == 0) errors.Add("slug", "Slug is required.");
        else if (slug.Length > 80 || !SlugPattern().IsMatch(slug)) errors.Add("slug", "Use lower-case letters, numbers and hyphens, e.g. pacific-blue.");
        errors.AddIf(d.Berth < 1, "berth", "Berth must be at least 1.");
        errors.AddIf(d.AdultBeds is < 0, "adultBeds", "Adult beds can't be negative.");
        errors.AddIf(d.KidsBeds is < 0, "kidsBeds", "Kids beds can't be negative.");
        var beds = (d.AdultBeds ?? 0) + (d.KidsBeds ?? 0);
        if (d.Berth >= 1 && d.Berth < beds) errors.Add("berth", $"Berth can't be less than adult beds + kids beds ({beds}).");
        var bedding = errors.Capture(() => Text.Optional(d.BeddingDescription, "beddingDescription", "Bedding description", 500));
        errors.AddIf(d.SecurityBond < 0, "securityBond", "Security bond can't be negative.");
        errors.AddIf(d.DepositAmount is <= 0, "depositAmount", "Deposit must be more than $0, or left empty to use the default.");
        if (!IsValidImageUrl(d.ImageUrl))
            errors.Add("imageUrl", "Use an https:// link to an image on bergerhouseboats.com.au.");
        errors.ThrowIfAny();

        Name = name!;
        Slug = slug;
        Type = d.Type;
        Berth = d.Berth;
        AdultBeds = d.AdultBeds;
        KidsBeds = d.KidsBeds;
        BeddingDescription = bedding;
        SecurityBond = Money.Round(d.SecurityBond);
        DepositAmount = d.DepositAmount is { } dep ? Money.Round(dep) : null;
        ImageUrl = string.IsNullOrWhiteSpace(d.ImageUrl) ? null : d.ImageUrl.Trim();
        HasSpa = d.HasSpa;
        IsPetFriendly = d.IsPetFriendly;
    }

    public void SetRates(IEnumerable<BoatRate> rates)
    {
        var list = new List<BoatRate>();
        var errors = new ValidationErrors();
        foreach (var r in rates)
        {
            if (!RatedPeriods.Contains(r.PeriodType)) { errors.Add("rates", $"{r.PeriodType} bookings have no rate; staff type the price."); continue; }
            if (r.Price < 0) { errors.Add("rates", "Rates can't be negative."); continue; }
            list.RemoveAll(x => x.SeasonId == r.SeasonId && x.PeriodType == r.PeriodType);
            list.Add(r with { Price = Money.Round(r.Price) });
        }
        errors.ThrowIfAny();
        Rates = list;
    }

    public void SetAllowedAddons(IEnumerable<string> addonIds) => AllowedAddonIds = addonIds.Distinct().ToList();

    public decimal? RateFor(string seasonId, PeriodType periodType) =>
        Rates.FirstOrDefault(r => r.SeasonId == seasonId && r.PeriodType == periodType)?.Price;

    /// <summary>The boat's own deposit, or the Settings default.</summary>
    public decimal EffectiveDeposit(decimal settingsDeposit) => DepositAmount ?? settingsDeposit;

    /// <summary>What still stops this boat from being activated; empty when it can be.</summary>
    public IReadOnlyList<string> MissingForActivation(IEnumerable<Season> seasons)
    {
        var missing = new List<string>();
        if (AdultBeds is null) missing.Add("Number of adult beds");
        if (KidsBeds is null) missing.Add("Number of kids beds (0 is allowed)");
        if (string.IsNullOrWhiteSpace(BeddingDescription)) missing.Add("Bedding description");
        foreach (var s in seasons)
            foreach (var p in RequiredRatePeriods)
                if (RateFor(s.Id, p) is null) missing.Add($"Rate: {s.Name} – {p}");
        return missing;
    }

    public void Activate(IEnumerable<Season> seasons)
    {
        var missing = MissingForActivation(seasons);
        if (missing.Count > 0)
            throw new DomainValidationException("isActive", "Can't activate until these are filled in: " + string.Join(", ", missing) + ".");
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    public bool IsBookableHouseBoat => IsActive && Type == BoatType.House;

    /// <summary>Boats are listed by berth (largest first), then name.</summary>
    public static IOrderedEnumerable<T> InDisplayOrder<T>(IEnumerable<T> boats, Func<T, int> berth, Func<T, string> name) =>
        boats.OrderByDescending(berth).ThenBy(name, StringComparer.OrdinalIgnoreCase);
}

public interface IBoatRepository
{
    Task<IReadOnlyList<Boat>> ListAsync(CancellationToken ct = default);
    Task<Boat?> GetAsync(BoatId id, CancellationToken ct = default);
    Task<Boat?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task SaveAsync(Boat boat, CancellationToken ct = default);
}
