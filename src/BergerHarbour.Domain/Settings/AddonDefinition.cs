using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>An add-on in the catalogue (yabby pumps, ice, outboard motor, car parking, …).</summary>
public sealed class AddonDefinition : AggregateRoot<AddonId>
{
    private AddonDefinition(AddonId id, int version, string name, string? description, bool priceOnRequest,
        AddonPrices? prices, bool quantityApplies, int? capacity, string? capacityUnitLabel, int sortOrder, bool isActive)
        : base(id, version)
    {
        Name = name;
        Description = description;
        PriceOnRequest = priceOnRequest;
        Prices = prices;
        QuantityApplies = quantityApplies;
        Capacity = capacity;
        CapacityUnitLabel = capacityUnitLabel;
        SortOrder = sortOrder;
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

    /// <summary>
    /// When set, the add-on is limited to this many units in use per night, shared across all boats and bookings
    /// (e.g. 11 car spots).
    /// </summary>
    public int? Capacity { get; private set; }

    /// <summary>Used in the "remaining" message, e.g. "car spots".</summary>
    public string? CapacityUnitLabel { get; private set; }

    /// <summary>Position in the admin-defined order; changed only through reordering.</summary>
    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public bool HasCapacity => Capacity is not null;

    public static AddonDefinition Create(string name, string? description, bool priceOnRequest, AddonPrices? prices,
        bool quantityApplies, int? capacity, string? capacityUnitLabel, int sortOrder) =>
        CreateWithId(AddonId.New(), name, description, priceOnRequest, prices, quantityApplies, capacity,
            capacityUnitLabel, sortOrder);

    /// <summary>Seeds use stable ids so the seed is idempotent.</summary>
    public static AddonDefinition CreateWithId(AddonId id, string name, string? description, bool priceOnRequest,
        AddonPrices? prices, bool quantityApplies, int? capacity, string? capacityUnitLabel, int sortOrder)
    {
        var addon = new AddonDefinition(id, 0, string.Empty, null, true, null, false, null, null, sortOrder, true);
        addon.Update(name, description, priceOnRequest, prices, quantityApplies, capacity, capacityUnitLabel);
        return addon;
    }

    public void Update(string name, string? description, bool priceOnRequest, AddonPrices? prices, bool quantityApplies,
        int? capacity, string? capacityUnitLabel)
    {
        Name = Guard.Required(name, "name", "Name", 100);
        Description = Guard.Optional(description, "description", "Description", 500);
        if (!priceOnRequest && prices is null)
        {
            throw new DomainValidationException("prices", "Enter prices, or tick 'Price on request'.");
        }

        if (capacity is < 0)
        {
            throw new DomainValidationException("capacity", "Capacity cannot be negative.");
        }

        PriceOnRequest = priceOnRequest;
        Prices = priceOnRequest ? null : prices;
        QuantityApplies = quantityApplies;
        Capacity = capacity;
        CapacityUnitLabel = capacity is null ? null : Guard.Optional(capacityUnitLabel, "capacityUnitLabel", "Capacity unit label", 40);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;

    /// <summary>
    /// The unit price for a booking of the given period type, or null when staff enter it: the add-on is price on
    /// request, or the booking is a LongWeekend or Custom stay (add-ons have no prices for those).
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
            PeriodType.Weekend => Prices.Weekend,
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

    /// <summary>"6 car spots remaining", or "6 available for your dates" without a unit label.</summary>
    public string RemainingText(int remaining) =>
        CapacityUnitLabel is { } unit ? $"{remaining} {unit} remaining" : $"{remaining} available for your dates";

    public AddonDefinitionSnapshot ToSnapshot() =>
        new(Id.Value, Version, Name, Description, PriceOnRequest, Prices?.Midweek, Prices?.Weekend, Prices?.Week,
            QuantityApplies, Capacity, CapacityUnitLabel, SortOrder, IsActive);

    public static AddonDefinition FromSnapshot(AddonDefinitionSnapshot s) =>
        new(new AddonId(s.Id), s.Version, s.Name, s.Description, s.PriceOnRequest,
            s.PriceOnRequest || s.MidweekPrice is null || s.WeekendPrice is null || s.WeekPrice is null
                ? null
                : new AddonPrices(s.MidweekPrice.Value, s.WeekendPrice.Value, s.WeekPrice.Value),
            s.QuantityApplies, s.Capacity, s.CapacityUnitLabel, s.SortOrder, s.IsActive);
}

/// <summary>Rules that span the whole add-on catalogue.</summary>
public static class AddonCatalogue
{
    /// <summary>The admin-defined order, used in the booking app, the admin add-on picker and emails.</summary>
    public static IEnumerable<AddonDefinition> InDisplayOrder(IEnumerable<AddonDefinition> addons) =>
        addons.OrderBy(a => a.SortOrder).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>New add-ons go to the end.</summary>
    public static int NextSortOrder(IEnumerable<AddonDefinition> addons) =>
        addons.Select(a => a.SortOrder).DefaultIfEmpty(-1).Max() + 1;

    /// <summary>
    /// Applies a new order. <paramref name="orderedIds"/> must list every add-on exactly once. Returns the add-ons
    /// whose position changed.
    /// </summary>
    public static IReadOnlyList<AddonDefinition> Reorder(IReadOnlyCollection<AddonDefinition> addons,
        IReadOnlyList<AddonId> orderedIds)
    {
        var byId = addons.ToDictionary(a => a.Id);
        if (orderedIds.Count != byId.Count || orderedIds.Distinct().Count() != orderedIds.Count ||
            orderedIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new DomainValidationException("addonIds", "The add-on list has changed. Reload and try again.");
        }

        var changed = new List<AddonDefinition>();
        for (var position = 0; position < orderedIds.Count; position++)
        {
            var addon = byId[orderedIds[position]];
            if (addon.SortOrder != position)
            {
                addon.MoveTo(position);
                changed.Add(addon);
            }
        }

        return changed;
    }
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
    int? Capacity,
    string? CapacityUnitLabel,
    int SortOrder,
    bool IsActive);
