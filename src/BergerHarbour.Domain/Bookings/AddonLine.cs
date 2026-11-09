using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// An add-on on a booking. A line has no status: a line on the booking is accepted, and staff decline by deleting it.
/// <see cref="UnitPrice"/> is null until staff set it (price on request, LongWeekend and Custom bookings).
/// </summary>
public sealed record AddonLine
{
    private AddonLine(string id, AddonId addonId, string nameSnapshot, int quantity, decimal? unitPrice)
    {
        Id = id;
        AddonId = addonId;
        NameSnapshot = nameSnapshot;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Id { get; }

    public AddonId AddonId { get; }

    public string NameSnapshot { get; }

    public int Quantity { get; }

    public decimal? UnitPrice { get; }

    public decimal? LineTotal => UnitPrice is null ? null : Quantity * UnitPrice.Value;

    public static AddonLine Create(string? id, AddonId addonId, string nameSnapshot, int quantity, decimal? unitPrice, string field = "addonLines")
    {
        if (quantity < 1)
        {
            throw new DomainValidationException(field, "Each add-on needs a quantity of at least 1.");
        }

        if (unitPrice < 0)
        {
            throw new DomainValidationException(field, "Add-on prices can't be negative.");
        }

        return new AddonLine(string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id, addonId, nameSnapshot, quantity, unitPrice);
    }
}
