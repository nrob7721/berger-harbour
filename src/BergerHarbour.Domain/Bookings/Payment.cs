using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>A payment received for a booking.</summary>
public sealed record Payment(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    BookingCreator RecordedBy)
{
    public const int MaxNoteLength = 500;

    /// <summary>The Sydney date the payment was made.</summary>
    public DateOnly Date => Sydney.DateOf(PaidAt);

    /// <summary>
    /// A manual payment staff record (§7.3): an amount above $0 and a note of at most 500 characters. It is paid now when
    /// the date is today, otherwise at 12:00 Sydney on that date. The booking checks the date rule when it takes it.
    /// </summary>
    public static Payment Manual(decimal amount, DateOnly date, string? note, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        if (amount <= 0)
        {
            errors.Add("amount", "The amount must be more than $0.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxNoteLength)
        {
            errors.Add("note", "The note must be 500 characters or fewer.");
        }

        errors.ThrowIfAny();
        var paidAt = date == Sydney.DateOf(now) ? now : Sydney.InstantOf(date, new TimeOnly(12, 0));
        return new Payment(Guid.NewGuid().ToString("N"), amount, PaymentMethod.Manual, null, paidAt, trimmed, BookingCreator.SystemUser);
    }
}
