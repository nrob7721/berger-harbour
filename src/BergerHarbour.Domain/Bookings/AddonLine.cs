using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// An add-on on a booking. Lines have no status: a line on the booking is accepted, and staff decline an add-on by
/// deleting its line. A line without a price yet (price on request, LongWeekend/Custom bookings) does not count
/// toward the total until staff enter one.
/// </summary>
public sealed class AddonLine
{
    internal AddonLine(string id, AddonId addonId, string nameSnapshot, int quantity, decimal? unitPrice)
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

    public int Quantity { get; private set; }

    /// <summary>Null until staff set it.</summary>
    public decimal? UnitPrice { get; private set; }

    /// <summary>The amount this line adds to the booking total, or null while it has no price.</summary>
    public decimal? LineTotal => UnitPrice is { } price ? price * Quantity : null;

    internal static AddonLine Create(AddonId addonId, string name, int quantity, decimal? unitPrice) =>
        new(IdGenerator.NewId(), addonId, Guard.Required(name, "addons", "Add-on name"), ValidateQuantity(quantity),
            ValidatePrice(unitPrice));

    internal void Update(int quantity, decimal? unitPrice)
    {
        Quantity = ValidateQuantity(quantity);
        UnitPrice = ValidatePrice(unitPrice);
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

/// <summary>The contact details a customer entered online, kept on the booking until it becomes Active.</summary>
public sealed record BookingCustomerDetails
{
    public BookingCustomerDetails(string fullName, string email, string mobileNumber)
    {
        FullName = Guard.Required(fullName, "fullName", "Full name");
        Email = EmailAddress.Create(email).Value;
        MobileNumber = PhoneNumber.Normalise(mobileNumber, "mobile", "Mobile number");
    }

    public string FullName { get; }

    /// <summary>Normalised (lower-case, trimmed).</summary>
    public string Email { get; }

    /// <summary>E.164.</summary>
    public string MobileNumber { get; }

    /// <summary>Rebuilds stored details without re-validating them.</summary>
    public static BookingCustomerDetails Restore(string fullName, string email, string mobileNumber) =>
        new(fullName, email, mobileNumber, RestoreMarker.Instance);

    private BookingCustomerDetails(string fullName, string email, string mobileNumber, RestoreMarker _)
    {
        FullName = fullName;
        Email = email;
        MobileNumber = mobileNumber;
    }

    private sealed class RestoreMarker
    {
        public static readonly RestoreMarker Instance = new();
    }
}
