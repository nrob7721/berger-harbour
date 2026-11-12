using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>An add-on in the catalogue (e.g. Car parking). Optionally capacity-limited across all boats and bookings.</summary>
public sealed class AddonDefinition : AggregateRoot
{
    public const int MaxCapacityUnitLabelLength = 30;

    private AddonDefinition(AddonId id)
    {
        Id = id;
        Name = string.Empty;
        Prices = AddonPrices.None;
    }

    public AddonId Id { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public bool PriceOnRequest { get; private set; }

    public AddonPrices Prices { get; private set; }

    public bool QuantityApplies { get; private set; }

    public int? Capacity { get; private set; }

    public string? CapacityUnitLabel { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public bool HasCapacity => Capacity is not null;

    public static AddonDefinition Create(
        AddonId id, string? name, string? description, bool priceOnRequest, AddonPrices prices, bool quantityApplies, int? capacity,
        string? capacityUnitLabel, int sortOrder, bool isActive = true)
    {
        var addon = new AddonDefinition(id) { SortOrder = sortOrder };
        addon.Update(name, description, priceOnRequest, prices, quantityApplies, capacity, capacityUnitLabel, isActive);
        return addon;
    }

    public static AddonDefinition Rehydrate(
        AddonId id, string name, string? description, bool priceOnRequest, AddonPrices prices, bool quantityApplies, int? capacity,
        string? capacityUnitLabel, int sortOrder, bool isActive, int version)
    {
        var addon = new AddonDefinition(id)
        {
            Name = name,
            Description = description,
            PriceOnRequest = priceOnRequest,
            Prices = prices,
            QuantityApplies = quantityApplies,
            Capacity = capacity,
            CapacityUnitLabel = capacityUnitLabel,
            SortOrder = sortOrder,
            IsActive = isActive,
        };
        addon.SetPersistedVersion(version);
        return addon;
    }

    /// <summary>Price on request clears the prices; without a capacity the unit label is cleared.</summary>
    public void Update(
        string? name, string? description, bool priceOnRequest, AddonPrices prices, bool quantityApplies, int? capacity,
        string? capacityUnitLabel, bool isActive)
    {
        var errors = new ValidationErrors();
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
        {
            errors.Add("name", "Name is required.");
        }
        else if (trimmedName.Length > 100)
        {
            errors.Add("name", "Name must be 100 characters or fewer.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > 1000)
        {
            errors.Add("description", "Description must be 1000 characters or fewer.");
        }

        if (!priceOnRequest)
        {
            CheckPrice(errors, "midweekPrice", prices.Midweek);
            CheckPrice(errors, "weekendPrice", prices.Weekend);
            CheckPrice(errors, "weekPrice", prices.Week);
        }

        if (capacity < 1)
        {
            errors.Add("capacity", "Capacity must be at least 1.");
        }

        var label = capacity is null || string.IsNullOrWhiteSpace(capacityUnitLabel) ? null : capacityUnitLabel.Trim();
        if (capacity is not null && label is null)
        {
            errors.Add("capacityUnitLabel", "Required");
        }
        else if (label?.Length > MaxCapacityUnitLabelLength)
        {
            errors.Add("capacityUnitLabel", $"Capacity unit label must be {MaxCapacityUnitLabelLength} characters or fewer.");
        }

        errors.ThrowIfAny();
        Name = trimmedName;
        Description = trimmedDescription;
        PriceOnRequest = priceOnRequest;
        Prices = priceOnRequest ? AddonPrices.None : prices;
        QuantityApplies = quantityApplies;
        Capacity = capacity;
        CapacityUnitLabel = label;
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Only the ▲/▼ reorder sets the position.</summary>
    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    /// <summary>The unit price for a booking's period type, or null when staff must enter it.</summary>
    public decimal? PriceFor(PeriodType periodType) =>
        PriceOnRequest
            ? null
            : periodType switch
            {
                PeriodType.Midweek => Prices.Midweek,
                PeriodType.Weekend => Prices.Weekend,
                PeriodType.Week => Prices.Week,
                _ => null,
            };

    private static void CheckPrice(ValidationErrors errors, string field, decimal? price)
    {
        if (price is null)
        {
            errors.Add(field, "Enter a price, or tick Price on request.");
        }
        else if (price < 0)
        {
            errors.Add(field, "Price can't be negative.");
        }
    }
}
