using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>An add-on requested on a booking. Only Confirmed lines with a price count toward the total.</summary>
public sealed class AddonLine
{
    internal AddonLine(string id, AddonId addonId, string nameSnapshot, int quantity, decimal? unitPrice,
        AddonLineStatus status)
    {
        Id = id;
        AddonId = addonId;
        NameSnapshot = nameSnapshot;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Status = status;
    }

    public string Id { get; }

    public AddonId AddonId { get; }

    public string NameSnapshot { get; }

    public int Quantity { get; private set; }

    /// <summary>Null while the price is on request.</summary>
    public decimal? UnitPrice { get; private set; }

    public AddonLineStatus Status { get; private set; }

    /// <summary>The amount this line adds to the booking total.</summary>
    public decimal ChargedAmount => Status == AddonLineStatus.Confirmed && UnitPrice is { } price ? price * Quantity : 0m;

    /// <summary>The estimated amount shown to the customer while the line is still a request.</summary>
    public decimal? EstimatedAmount => UnitPrice is { } price ? price * Quantity : null;

    internal static AddonLine Create(AddonId addonId, string name, int quantity, decimal? unitPrice) =>
        new(IdGenerator.NewId(), addonId, Guard.Required(name, "addons", "Add-on name"), ValidateQuantity(quantity),
            ValidatePrice(unitPrice), AddonLineStatus.Requested);

    internal void Update(int quantity, decimal? unitPrice, AddonLineStatus status)
    {
        var price = ValidatePrice(unitPrice);
        if (status == AddonLineStatus.Confirmed && price is null)
        {
            throw new DomainValidationException("unitPrice", "Enter a unit price before confirming this add-on.");
        }

        Quantity = ValidateQuantity(quantity);
        UnitPrice = price;
        Status = status;
    }

    private static int ValidateQuantity(int quantity) => quantity >= 1
        ? quantity
        : throw new DomainValidationException("quantity", "Quantity must be at least 1.");

    private static decimal? ValidatePrice(decimal? price) =>
        price is { } p ? Guard.Money(p, "unitPrice", "Unit price") : null;
}

/// <summary>A payment received against a booking.</summary>
public sealed record Payment
{
    internal Payment(string id, decimal amount, PaymentMethod method, string? stripePaymentIntentId, DateTimeOffset paidAt,
        string? note, PaymentRecordedBy recordedBy)
    {
        Id = id;
        Amount = Guard.PositiveMoney(amount, "amount", "Payment amount");
        Method = method;
        StripePaymentIntentId = stripePaymentIntentId;
        PaidAt = paidAt;
        Note = Guard.Optional(note, "note", "Note", 500);
        RecordedBy = recordedBy;
    }

    public string Id { get; }

    public decimal Amount { get; }

    public PaymentMethod Method { get; }

    public string? StripePaymentIntentId { get; }

    public DateTimeOffset PaidAt { get; }

    public string? Note { get; }

    public PaymentRecordedBy RecordedBy { get; }
}

/// <summary>The customer accepted the rooming warning that was shown to them.</summary>
public sealed record RoomingWarningAcceptance(DateTimeOffset AcceptedAt, string WarningTextShown);

/// <summary>A notification already sent, used to keep scheduled emails idempotent.</summary>
public sealed record SentNotification(string Key, DateTimeOffset SentAt);
