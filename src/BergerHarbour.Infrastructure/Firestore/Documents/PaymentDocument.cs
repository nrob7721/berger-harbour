using BergerHarbour.Domain.Bookings;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

[FirestoreData]
public sealed class PaymentDocument
{
    [FirestoreProperty("id")]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("amountCents")]
    public long AmountCents { get; set; }

    [FirestoreProperty("method")]
    public string Method { get; set; } = string.Empty;

    [FirestoreProperty("stripePaymentIntentId")]
    public string? StripePaymentIntentId { get; set; }

    [FirestoreProperty("paidAt")]
    public Timestamp PaidAt { get; set; }

    [FirestoreProperty("note")]
    public string? Note { get; set; }

    [FirestoreProperty("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    public static PaymentDocument From(Payment p) => new()
    {
        Id = p.Id,
        AmountCents = FirestoreValues.Cents(p.Amount),
        Method = p.Method.ToString(),
        StripePaymentIntentId = p.StripePaymentIntentId,
        PaidAt = FirestoreValues.Instant(p.PaidAt),
        Note = p.Note,
        RecordedBy = p.RecordedBy.ToString(),
    };

    public Payment ToDomain() => new(Id, FirestoreValues.Money(AmountCents), Enum.Parse<PaymentMethod>(Method), StripePaymentIntentId,
        FirestoreValues.Instant(PaidAt), Note, Enum.Parse<BookingCreator>(RecordedBy));
}
