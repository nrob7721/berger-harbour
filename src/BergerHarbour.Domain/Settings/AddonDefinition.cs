using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>Add-on prices per period type. There are no add-on prices for LongWeekend or Custom bookings.</summary>
public sealed record AddonPrices(decimal Midweek, decimal Weekend, decimal Week)
{
    public decimal? For(PeriodType periodType) => periodType switch
    {
        PeriodType.Midweek => Midweek,
        PeriodType.Weekend => Weekend,
        PeriodType.Week => Week,
        _ => null,
    };
}

public sealed record AddonDetails(
    string Name,
    string? Description,
    bool PriceOnRequest,
    decimal? MidweekPrice,
    decimal? WeekendPrice,
    decimal? WeekPrice,
    bool QuantityApplies,
    int? Capacity,
    string? CapacityUnitLabel);

public sealed class AddonDefinition : AggregateRoot
{
    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public bool PriceOnRequest { get; private set; }
    /// <summary>Null when price on request.</summary>
    public AddonPrices? Prices { get; private set; }
    public bool QuantityApplies { get; private set; }
    public int? Capacity { get; private set; }
    public string? CapacityUnitLabel { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public bool HasCapacity => Capacity is not null;

    private AddonDefinition() { }

    public static AddonDefinition Create(AddonDetails details, int sortOrder, DateTimeOffset now)
    {
        var a = new AddonDefinition { Id = Ids.New(), SortOrder = sortOrder, IsActive = true };
        a.Update(details);
        a.InitialiseAudit(now);
        return a;
    }

    public static AddonDefinition Rehydrate(string id, string name, string? description, bool priceOnRequest, AddonPrices? prices,
        bool quantityApplies, int? capacity, string? capacityUnitLabel, int sortOrder, bool isActive,
        int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var a = new AddonDefinition
        {
            Id = id, Name = name, Description = description, PriceOnRequest = priceOnRequest, Prices = priceOnRequest ? null : prices,
            QuantityApplies = quantityApplies, Capacity = capacity, CapacityUnitLabel = capacity is null ? null : capacityUnitLabel,
            SortOrder = sortOrder, IsActive = isActive,
        };
        a.RestoreAudit(version, created, modified);
        return a;
    }

    public void Update(AddonDetails d)
    {
        var errors = new ValidationErrors();
        var name = errors.Capture(() => Text.Required(d.Name, "name", "Name", 100));
        var description = errors.Capture(() => Text.Optional(d.Description, "description", "Description", 1000));
        AddonPrices? prices = null;
        if (!d.PriceOnRequest)
        {
            void Check(decimal? v, string field)
            {
                if (v is null) errors.Add(field, "Enter a price, or tick Price on request.");
                else if (v < 0) errors.Add(field, "Price can't be negative.");
            }
            Check(d.MidweekPrice, "midweekPrice");
            Check(d.WeekendPrice, "weekendPrice");
            Check(d.WeekPrice, "weekPrice");
            if (d.MidweekPrice is { } m && d.WeekendPrice is { } we && d.WeekPrice is { } w)
                prices = new AddonPrices(Money.Round(m), Money.Round(we), Money.Round(w));
        }
        if (d.Capacity is < 0) errors.Add("capacity", "Capacity can't be negative.");
        var label = d.Capacity is null ? null : errors.Capture(() => Text.Optional(d.CapacityUnitLabel, "capacityUnitLabel", "Capacity unit label", 40));
        errors.ThrowIfAny();

        Name = name!;
        Description = description;
        PriceOnRequest = d.PriceOnRequest;
        Prices = d.PriceOnRequest ? null : prices; // price on request clears any prices sent
        QuantityApplies = d.QuantityApplies;
        Capacity = d.Capacity;
        CapacityUnitLabel = label; // cleared without a capacity
    }

    public decimal? PriceFor(PeriodType periodType) => PriceOnRequest ? null : Prices?.For(periodType);

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    /// <summary>"6 car spots remaining" or "6 available for your dates".</summary>
    public string RemainingText(int remaining) =>
        string.IsNullOrWhiteSpace(CapacityUnitLabel) ? $"{remaining} available for your dates" : $"{remaining} {CapacityUnitLabel} remaining";
}

public interface IAddonDefinitionRepository
{
    /// <summary>All add-ons in admin-defined order.</summary>
    Task<IReadOnlyList<AddonDefinition>> ListAsync(CancellationToken ct = default);
    Task<AddonDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(AddonDefinition addon, CancellationToken ct = default);
    Task SaveAllAsync(IEnumerable<AddonDefinition> addons, CancellationToken ct = default);
}
