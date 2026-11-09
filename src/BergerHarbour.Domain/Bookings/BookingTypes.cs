using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

public enum BookingCreator { Customer, SystemUser }
public enum BookingStatus { PendingPayment, Active, Superseded, Cancelled, Expired }
public enum PaymentScheduleType { Standard, Extended }
public enum PaymentMethod { Stripe, Manual }
public enum PaymentStatus { Outstanding, DepositPaid, PartPaid, FullyPaid }

/// <summary>Adults (≥ 1) and children under 12 (≥ 0).</summary>
public sealed record GuestNumbers
{
    public int Adults { get; }
    public int Children { get; }
    public int Total => Adults + Children;

    public GuestNumbers(int adults, int children)
    {
        var errors = new ValidationErrors();
        errors.AddIf(adults < 1, "numberOfAdults", "At least 1 adult is required.");
        errors.AddIf(children < 0, "numberOfChildren", "Children can't be negative.");
        errors.AddIf(adults > 100, "numberOfAdults", "That's too many guests.");
        errors.AddIf(children > 100, "numberOfChildren", "That's too many guests.");
        errors.ThrowIfAny();
        Adults = adults;
        Children = children;
    }
}

/// <summary>Contact details the customer entered online.</summary>
public sealed record CustomerDetails(string FirstName, string LastName, string Email, string MobileNumber)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>A requested add-on on a booking. A line on the booking is accepted; staff decline by deleting it.</summary>
public sealed record AddonLine(string Id, string AddonId, string NameSnapshot, int Quantity, decimal? UnitPrice)
{
    public decimal? LineTotal => UnitPrice is { } p ? Money.Round(p * Quantity) : null;

    public static AddonLine Create(string? id, string addonId, string name, int quantity, decimal? unitPrice, string field = "addonLines")
    {
        if (quantity < 1) throw new DomainValidationException(field, "Quantity must be at least 1.");
        if (unitPrice is < 0) throw new DomainValidationException(field, "Unit price can't be negative.");
        return new AddonLine(string.IsNullOrWhiteSpace(id) ? Ids.New() : id, addonId, name, quantity, unitPrice is { } p ? Money.Round(p) : null);
    }
}

public sealed record Payment(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    BookingCreator RecordedBy);

public sealed record RoomingWarningAcceptance(DateTimeOffset AcceptedAt, string WarningTextShown);

public sealed record SentNotification(string Key, DateTimeOffset SentAt);
