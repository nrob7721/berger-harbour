using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>An add-on in the catalogue (yabby pumps, ice, outboard motor, …).</summary>
public sealed class AddonDefinition : AggregateRoot<AddonId>
{
    private AddonDefinition(AddonId id, int version, string name, string? description, bool priceOnRequest,
        AddonPrices? prices, bool quantityApplies, bool isActive)
        : base(id, version)
    {
        Name = name;
        Description = description;
        PriceOnRequest = priceOnRequest;
        Prices = prices;
        QuantityApplies = quantityApplies;
        IsActive = isActive;
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>When true, staff set the price on each booking line.</summary>
    public bool PriceOnRequest { get; private set; }

    /// <summary>Prices by period type; null when <see cref="PriceOnRequest"/>.</summary>
    public AddonPrices? Prices { get; private set; }

    /// <summary>When false, the quantity is always 1.</summary>
    public bool QuantityApplies { get; private set; }

    public bool IsActive { get; private set; }

    public static AddonDefinition Create(string name, string? description, bool priceOnRequest, AddonPrices? prices,
        bool quantityApplies)
    {
        var addon = new AddonDefinition(AddonId.New(), 0, string.Empty, null, true, null, false, true);
        addon.Update(name, description, priceOnRequest, prices, quantityApplies);
        return addon;
    }

    /// <summary>Seeds use stable ids so the seed is idempotent.</summary>
    public static AddonDefinition CreateWithId(AddonId id, string name, string? description, bool priceOnRequest,
        AddonPrices? prices, bool quantityApplies)
    {
        var addon = new AddonDefinition(id, 0, string.Empty, null, true, null, false, true);
        addon.Update(name, description, priceOnRequest, prices, quantityApplies);
        return addon;
    }

    public void Update(string name, string? description, bool priceOnRequest, AddonPrices? prices, bool quantityApplies)
    {
        Name = Guard.Required(name, "name", "Name", 100);
        Description = Guard.Optional(description, "description", "Description", 500);
        if (!priceOnRequest && prices is null)
        {
            throw new DomainValidationException("prices", "Enter prices, or tick 'Price on request'.");
        }

        PriceOnRequest = priceOnRequest;
        Prices = priceOnRequest ? null : prices;
        QuantityApplies = quantityApplies;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// The unit price for a booking of the given period type, or null when the price is on request.
    /// Long weekends use the weekend price; custom stays are priced by staff.
    /// </summary>
    public decimal? UnitPriceFor(PeriodType periodType)
    {
        if (PriceOnRequest || Prices is null)
        {
            return null;
        }

        return periodType switch
        {
            PeriodType.Midweek => Prices.Midweek,
            PeriodType.Weekend or PeriodType.LongWeekend => Prices.Weekend,
            PeriodType.Week => Prices.Week,
            _ => null,
        };
    }

    public int NormaliseQuantity(int quantity)
    {
        if (!QuantityApplies)
        {
            return 1;
        }

        if (quantity < 1)
        {
            throw new DomainValidationException("quantity", "Quantity must be at least 1.");
        }

        return quantity;
    }

    public AddonDefinitionSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, Description, PriceOnRequest, Prices?.Midweek, Prices?.Weekend, Prices?.Week,
            QuantityApplies, IsActive);

    public static AddonDefinition FromSnapshot(AddonDefinitionSnapshot s) =>
        new(new AddonId(s.Id), s.Version, s.Name, s.Description, s.PriceOnRequest,
            s.PriceOnRequest || s.MidweekPrice is null || s.WeekendPrice is null || s.WeekPrice is null
                ? null
                : new AddonPrices(s.MidweekPrice.Value, s.WeekendPrice.Value, s.WeekPrice.Value),
            s.QuantityApplies, s.IsActive);
}

public sealed record AddonPrices
{
    public AddonPrices(decimal midweek, decimal weekend, decimal week)
    {
        Midweek = Guard.Money(midweek, "prices.midweek", "Mid-week price");
        Weekend = Guard.Money(weekend, "prices.weekend", "Weekend price");
        Week = Guard.Money(week, "prices.week", "Week price");
    }

    public decimal Midweek { get; }

    public decimal Weekend { get; }

    public decimal Week { get; }
}

public sealed record AddonDefinitionSnapshot(
    string Id,
    int Version,
    string Name,
    string? Description,
    bool PriceOnRequest,
    decimal? MidweekPrice,
    decimal? WeekendPrice,
    decimal? WeekPrice,
    bool QuantityApplies,
    bool IsActive);
