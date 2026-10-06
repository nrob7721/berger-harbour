using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore.Documents;

// Firestore document shapes. Money is stored as integer cents, dates as yyyy-MM-dd strings (which sort correctly
// for range queries), instants as UTC timestamps and enums as their names.

[FirestoreData]
public sealed class BookingDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("reference")] public string Reference { get; set; } = "";
    [FirestoreProperty("createdBy")] public string CreatedBy { get; set; } = "";
    [FirestoreProperty("isStandby")] public bool IsStandby { get; set; }
    [FirestoreProperty("status")] public string Status { get; set; } = "";
    [FirestoreProperty("customerId")] public string CustomerId { get; set; } = "";
    [FirestoreProperty("boatId")] public string BoatId { get; set; } = "";
    [FirestoreProperty("periodType")] public string PeriodType { get; set; } = "";
    [FirestoreProperty("startDate")] public string StartDate { get; set; } = "";
    [FirestoreProperty("endDate")] public string EndDate { get; set; } = "";
    [FirestoreProperty("numberOfGuests")] public int NumberOfGuests { get; set; }
    [FirestoreProperty("comments")] public string? Comments { get; set; }
    [FirestoreProperty("hirePriceCents")] public long HirePriceCents { get; set; }
    [FirestoreProperty("depositAmountCents")] public long DepositAmountCents { get; set; }
    [FirestoreProperty("paymentSchedule")] public string PaymentSchedule { get; set; } = "";
    [FirestoreProperty("addonLines")] public List<AddonLineDocument> AddonLines { get; set; } = [];
    [FirestoreProperty("payments")] public List<PaymentDocument> Payments { get; set; } = [];
    [FirestoreProperty("holdExpiresAt")] public Timestamp? HoldExpiresAt { get; set; }
    [FirestoreProperty("stripeCheckoutSessionId")] public string? StripeCheckoutSessionId { get; set; }
    [FirestoreProperty("roomingWarningAcceptedAt")] public Timestamp? RoomingWarningAcceptedAt { get; set; }
    [FirestoreProperty("roomingWarningText")] public string? RoomingWarningText { get; set; }
    [FirestoreProperty("termsAcceptedAt")] public Timestamp? TermsAcceptedAt { get; set; }
    [FirestoreProperty("groupRestrictionDeclaredNotApplicable")] public bool? GroupRestrictionDeclaredNotApplicable { get; set; }
    [FirestoreProperty("paymentLinkTokenHash")] public string PaymentLinkTokenHash { get; set; } = "";
    [FirestoreProperty("sentNotifications")] public List<SentNotificationDocument> SentNotifications { get; set; } = [];
    [FirestoreProperty("createdDate")] public Timestamp CreatedDate { get; set; }
    [FirestoreProperty("modifiedDate")] public Timestamp ModifiedDate { get; set; }
}

[FirestoreData]
public sealed class AddonLineDocument
{
    [FirestoreProperty("id")] public string Id { get; set; } = "";
    [FirestoreProperty("addonId")] public string AddonId { get; set; } = "";
    [FirestoreProperty("nameSnapshot")] public string NameSnapshot { get; set; } = "";
    [FirestoreProperty("quantity")] public int Quantity { get; set; }
    [FirestoreProperty("unitPriceCents")] public long? UnitPriceCents { get; set; }
    [FirestoreProperty("status")] public string Status { get; set; } = "";
}

[FirestoreData]
public sealed class PaymentDocument
{
    [FirestoreProperty("id")] public string Id { get; set; } = "";
    [FirestoreProperty("amountCents")] public long AmountCents { get; set; }
    [FirestoreProperty("method")] public string Method { get; set; } = "";
    [FirestoreProperty("stripePaymentIntentId")] public string? StripePaymentIntentId { get; set; }
    [FirestoreProperty("paidAt")] public Timestamp PaidAt { get; set; }
    [FirestoreProperty("note")] public string? Note { get; set; }
    [FirestoreProperty("recordedBy")] public string RecordedBy { get; set; } = "";
}

[FirestoreData]
public sealed class SentNotificationDocument
{
    [FirestoreProperty("key")] public string Key { get; set; } = "";
    [FirestoreProperty("sentAt")] public Timestamp SentAt { get; set; }
}

[FirestoreData]
public sealed class BoatDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("slug")] public string Slug { get; set; } = "";
    [FirestoreProperty("type")] public string Type { get; set; } = "";
    [FirestoreProperty("maxNoOfGuests")] public int MaxNoOfGuests { get; set; }
    [FirestoreProperty("noOfBeds")] public int? NoOfBeds { get; set; }
    [FirestoreProperty("beddingDescription")] public string? BeddingDescription { get; set; }
    [FirestoreProperty("securityBondCents")] public long SecurityBondCents { get; set; }
    [FirestoreProperty("rates")] public List<BoatRateDocument> Rates { get; set; } = [];
    [FirestoreProperty("allowedAddonIds")] public List<string> AllowedAddonIds { get; set; } = [];
    [FirestoreProperty("isActive")] public bool IsActive { get; set; }
    [FirestoreProperty("createdDate")] public Timestamp CreatedDate { get; set; }
    [FirestoreProperty("modifiedDate")] public Timestamp ModifiedDate { get; set; }
}

[FirestoreData]
public sealed class BoatRateDocument
{
    [FirestoreProperty("seasonId")] public string SeasonId { get; set; } = "";
    [FirestoreProperty("periodType")] public string PeriodType { get; set; } = "";
    [FirestoreProperty("priceCents")] public long PriceCents { get; set; }
}

[FirestoreData]
public sealed class CustomerDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("fullName")] public string FullName { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("mobileNumber")] public string? MobileNumber { get; set; }
    [FirestoreProperty("createdDate")] public Timestamp CreatedDate { get; set; }
    [FirestoreProperty("modifiedDate")] public Timestamp ModifiedDate { get; set; }
}

[FirestoreData]
public sealed class CustomerEmailDocument
{
    [FirestoreProperty("customerId")] public string CustomerId { get; set; } = "";
}

[FirestoreData]
public sealed class UnavailabilityDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("boatId")] public string BoatId { get; set; } = "";
    [FirestoreProperty("firstNight")] public string FirstNight { get; set; } = "";
    [FirestoreProperty("lastNight")] public string LastNight { get; set; } = "";
    [FirestoreProperty("comments")] public string? Comments { get; set; }
    [FirestoreProperty("createdDate")] public Timestamp CreatedDate { get; set; }
    [FirestoreProperty("modifiedDate")] public Timestamp ModifiedDate { get; set; }
}

[FirestoreData]
public sealed class SeasonDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("ranges")] public List<SeasonRangeDocument> Ranges { get; set; } = [];
}

[FirestoreData]
public sealed class SeasonRangeDocument
{
    [FirestoreProperty("startDayMonth")] public string StartDayMonth { get; set; } = "";
    [FirestoreProperty("endDayMonth")] public string EndDayMonth { get; set; } = "";
}

[FirestoreData]
public sealed class BlockedPeriodDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("firstNight")] public string FirstNight { get; set; } = "";
    [FirestoreProperty("lastNight")] public string LastNight { get; set; } = "";
    [FirestoreProperty("usesExtendedPaymentSchedule")] public bool UsesExtendedPaymentSchedule { get; set; }
}

[FirestoreData]
public sealed class AddonDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("description")] public string? Description { get; set; }
    [FirestoreProperty("priceOnRequest")] public bool PriceOnRequest { get; set; }
    [FirestoreProperty("midweekPriceCents")] public long? MidweekPriceCents { get; set; }
    [FirestoreProperty("weekendPriceCents")] public long? WeekendPriceCents { get; set; }
    [FirestoreProperty("weekPriceCents")] public long? WeekPriceCents { get; set; }
    [FirestoreProperty("quantityApplies")] public bool QuantityApplies { get; set; }
    [FirestoreProperty("isActive")] public bool IsActive { get; set; }
}

[FirestoreData]
public sealed class EmailTemplateDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("subject")] public string Subject { get; set; } = "";
    [FirestoreProperty("htmlBody")] public string HtmlBody { get; set; } = "";
}

[FirestoreData]
public sealed class BusinessSettingsDocument
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("depositAmountCents")] public long DepositAmountCents { get; set; }
    [FirestoreProperty("minimumLeadTimeDays")] public int MinimumLeadTimeDays { get; set; }
    [FirestoreProperty("bookingsOpenUntil")] public string BookingsOpenUntil { get; set; } = "";
    [FirestoreProperty("staffAlertEmail")] public string StaffAlertEmail { get; set; } = "";
    [FirestoreProperty("contactPhone")] public string ContactPhone { get; set; } = "";
    [FirestoreProperty("contactEmail")] public string ContactEmail { get; set; } = "";
    [FirestoreProperty("hireTermsUrl")] public string HireTermsUrl { get; set; } = "";
}
