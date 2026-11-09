using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

/// <summary>A booking. <c>addonIds</c> is denormalised so capacity queries can use array-contains.</summary>
[FirestoreData]
public sealed class BookingDocument : IVersionedDocument
{
    [FirestoreProperty("reference")]
    public string? Reference { get; set; }

    [FirestoreProperty("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [FirestoreProperty("isStandby")]
    public bool IsStandby { get; set; }

    [FirestoreProperty("status")]
    public string Status { get; set; } = string.Empty;

    [FirestoreProperty("activatedAt")]
    public Timestamp? ActivatedAt { get; set; }

    [FirestoreProperty("customerId")]
    public string? CustomerId { get; set; }

    [FirestoreProperty("customerDetails")]
    public CustomerDetailsDocument? CustomerDetails { get; set; }

    [FirestoreProperty("bookingDraftId")]
    public string? BookingDraftId { get; set; }

    [FirestoreProperty("boatId")]
    public string BoatId { get; set; } = string.Empty;

    [FirestoreProperty("periodType")]
    public string PeriodType { get; set; } = string.Empty;

    [FirestoreProperty("startDate")]
    public string StartDate { get; set; } = string.Empty;

    [FirestoreProperty("endDate")]
    public string EndDate { get; set; } = string.Empty;

    [FirestoreProperty("numberOfAdults")]
    public int NumberOfAdults { get; set; }

    [FirestoreProperty("numberOfChildren")]
    public int NumberOfChildren { get; set; }

    [FirestoreProperty("checkInTime")]
    public string CheckInTime { get; set; } = "11:00";

    [FirestoreProperty("checkOutTime")]
    public string CheckOutTime { get; set; } = "08:00";

    [FirestoreProperty("bookingComments")]
    public string? BookingComments { get; set; }

    [FirestoreProperty("hirePriceCents")]
    public long HirePriceCents { get; set; }

    [FirestoreProperty("depositAmountCents")]
    public long DepositAmountCents { get; set; }

    [FirestoreProperty("paymentSchedule")]
    public string PaymentSchedule { get; set; } = string.Empty;

    [FirestoreProperty("addonLines")]
    public List<AddonLineDocument> AddonLines { get; set; } = [];

    [FirestoreProperty("addonIds")]
    public List<string> AddonIds { get; set; } = [];

    [FirestoreProperty("payments")]
    public List<PaymentDocument> Payments { get; set; } = [];

    [FirestoreProperty("holdExpiresAt")]
    public Timestamp? HoldExpiresAt { get; set; }

    [FirestoreProperty("stripeCheckoutSessionId")]
    public string? StripeCheckoutSessionId { get; set; }

    [FirestoreProperty("roomingWarningAcceptance")]
    public RoomingWarningAcceptanceDocument? RoomingWarningAcceptance { get; set; }

    [FirestoreProperty("termsAcceptedAt")]
    public Timestamp? TermsAcceptedAt { get; set; }

    [FirestoreProperty("paymentLinkTokenHash")]
    public string PaymentLinkTokenHash { get; set; } = string.Empty;

    [FirestoreProperty("sentNotifications")]
    public List<SentNotificationDocument> SentNotifications { get; set; } = [];

    [FirestoreProperty("createdDate")]
    public Timestamp CreatedDate { get; set; }

    [FirestoreProperty("modifiedDate")]
    public Timestamp ModifiedDate { get; set; }

    [FirestoreProperty("version")]
    public int Version { get; set; }

    public static BookingDocument From(Booking b) => new()
    {
        Reference = b.Reference?.Value,
        CreatedBy = b.CreatedBy.ToString(),
        IsStandby = b.IsStandby,
        Status = b.Status.ToString(),
        ActivatedAt = FirestoreValues.Instant(b.ActivatedAt),
        CustomerId = b.CustomerId?.Value,
        CustomerDetails = b.CustomerDetails is null ? null : CustomerDetailsDocument.From(b.CustomerDetails),
        BookingDraftId = b.BookingDraftId?.Value,
        BoatId = b.BoatId.Value,
        PeriodType = b.PeriodType.ToString(),
        StartDate = FirestoreValues.Date(b.StartDate),
        EndDate = FirestoreValues.Date(b.EndDate),
        NumberOfAdults = b.NumberOfAdults,
        NumberOfChildren = b.NumberOfChildren,
        CheckInTime = FirestoreValues.Time(b.CheckInTime),
        CheckOutTime = FirestoreValues.Time(b.CheckOutTime),
        BookingComments = b.BookingComments,
        HirePriceCents = FirestoreValues.Cents(b.HirePrice),
        DepositAmountCents = FirestoreValues.Cents(b.DepositAmount),
        PaymentSchedule = b.PaymentSchedule.ToString(),
        AddonLines = b.AddonLines.Select(AddonLineDocument.From).ToList(),
        AddonIds = b.AddonLines.Select(l => l.AddonId.Value).Distinct().ToList(),
        Payments = b.Payments.Select(PaymentDocument.From).ToList(),
        HoldExpiresAt = FirestoreValues.Instant(b.HoldExpiresAt),
        StripeCheckoutSessionId = b.StripeCheckoutSessionId,
        RoomingWarningAcceptance = b.RoomingWarningAcceptance is null ? null : RoomingWarningAcceptanceDocument.From(b.RoomingWarningAcceptance),
        TermsAcceptedAt = FirestoreValues.Instant(b.TermsAcceptedAt),
        PaymentLinkTokenHash = b.PaymentLinkTokenHash,
        SentNotifications = b.SentNotifications.Select(SentNotificationDocument.From).ToList(),
        CreatedDate = FirestoreValues.Instant(b.CreatedDate),
        ModifiedDate = FirestoreValues.Instant(b.ModifiedDate),
        Version = b.Version,
    };

    public Booking ToDomain(string id) => Booking.Rehydrate(new BookingState(
        new BookingId(id),
        Reference is null ? null : BookingReference.Parse(Reference),
        Enum.Parse<BookingCreator>(CreatedBy),
        IsStandby,
        Enum.Parse<BookingStatus>(Status),
        FirestoreValues.Instant(ActivatedAt),
        CustomerId is null ? null : new CustomerId(CustomerId),
        CustomerDetails?.ToDomain(),
        BookingDraftId is null ? null : new BookingDraftId(BookingDraftId),
        new BoatId(BoatId),
        Enum.Parse<PeriodType>(PeriodType),
        StayRange.Create(FirestoreValues.Date(StartDate), FirestoreValues.Date(EndDate)),
        NumberOfAdults,
        NumberOfChildren,
        FirestoreValues.Time(CheckInTime),
        FirestoreValues.Time(CheckOutTime),
        BookingComments,
        FirestoreValues.Money(HirePriceCents),
        FirestoreValues.Money(DepositAmountCents),
        Enum.Parse<PaymentScheduleType>(PaymentSchedule),
        AddonLines.Select(l => l.ToDomain()).ToList(),
        Payments.Select(p => p.ToDomain()).ToList(),
        FirestoreValues.Instant(HoldExpiresAt),
        StripeCheckoutSessionId,
        RoomingWarningAcceptance?.ToDomain(),
        FirestoreValues.Instant(TermsAcceptedAt),
        PaymentLinkTokenHash,
        SentNotifications.Select(n => n.ToDomain()).ToList(),
        FirestoreValues.Instant(CreatedDate),
        FirestoreValues.Instant(ModifiedDate),
        Version));
}
