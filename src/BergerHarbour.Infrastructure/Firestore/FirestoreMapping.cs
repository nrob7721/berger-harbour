using System.Globalization;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using BergerHarbour.Infrastructure.Firestore.Documents;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>Maps domain snapshots to and from Firestore documents.</summary>
internal static class FirestoreMapping
{
    public static long Cents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal Money(long cents) => cents / 100m;

    public static decimal? Money(long? cents) => cents is { } c ? c / 100m : null;

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Timestamp Ts(DateTimeOffset instant) => Timestamp.FromDateTimeOffset(instant);

    public static Timestamp? Ts(DateTimeOffset? instant) => instant is { } i ? Timestamp.FromDateTimeOffset(i) : null;

    public static DateTimeOffset Instant(Timestamp ts) => ts.ToDateTimeOffset();

    public static DateTimeOffset? Instant(Timestamp? ts) => ts?.ToDateTimeOffset();

    private static T Enum<T>(string value) where T : struct, System.Enum => System.Enum.Parse<T>(value);

    // ---- Booking --------------------------------------------------------------------------------------------------

    public static BookingDocument ToDocument(BookingSnapshot s, int version) => new()
    {
        Version = version,
        Reference = s.Reference,
        CreatedBy = s.CreatedBy.ToString(),
        IsStandby = s.IsStandby,
        Status = s.Status.ToString(),
        CustomerId = s.CustomerId,
        BoatId = s.BoatId,
        PeriodType = s.PeriodType.ToString(),
        StartDate = Date(s.StartDate),
        EndDate = Date(s.EndDate),
        NumberOfGuests = s.NumberOfGuests,
        Comments = s.Comments,
        HirePriceCents = Cents(s.HirePrice),
        DepositAmountCents = Cents(s.DepositAmount),
        PaymentSchedule = s.PaymentSchedule.ToString(),
        AddonLines = s.AddonLines.Select(l => new AddonLineDocument
        {
            Id = l.Id,
            AddonId = l.AddonId,
            NameSnapshot = l.NameSnapshot,
            Quantity = l.Quantity,
            UnitPriceCents = l.UnitPrice is { } p ? Cents(p) : null,
            Status = l.Status.ToString(),
        }).ToList(),
        Payments = s.Payments.Select(p => new PaymentDocument
        {
            Id = p.Id,
            AmountCents = Cents(p.Amount),
            Method = p.Method.ToString(),
            StripePaymentIntentId = p.StripePaymentIntentId,
            PaidAt = Ts(p.PaidAt),
            Note = p.Note,
            RecordedBy = p.RecordedBy.ToString(),
        }).ToList(),
        HoldExpiresAt = Ts(s.HoldExpiresAt),
        StripeCheckoutSessionId = s.StripeCheckoutSessionId,
        RoomingWarningAcceptedAt = Ts(s.RoomingWarningAcceptedAt),
        RoomingWarningText = s.RoomingWarningText,
        TermsAcceptedAt = Ts(s.TermsAcceptedAt),
        GroupRestrictionDeclaredNotApplicable = s.GroupRestrictionDeclaredNotApplicable,
        PaymentLinkTokenHash = s.PaymentLinkTokenHash,
        SentNotifications = s.SentNotifications.Select(n => new SentNotificationDocument { Key = n.Key, SentAt = Ts(n.SentAt) }).ToList(),
        CreatedDate = Ts(s.CreatedDate),
        ModifiedDate = Ts(s.ModifiedDate),
    };

    public static Booking ToBooking(string id, BookingDocument d) => Booking.FromSnapshot(new BookingSnapshot(
        id, d.Version, d.Reference, Enum<BookingCreatedBy>(d.CreatedBy), d.IsStandby, Enum<BookingStatus>(d.Status),
        d.CustomerId, d.BoatId, Enum<PeriodType>(d.PeriodType), Date(d.StartDate), Date(d.EndDate), d.NumberOfGuests,
        d.Comments, Money(d.HirePriceCents), Money(d.DepositAmountCents), Enum<PaymentSchedule>(d.PaymentSchedule),
        d.AddonLines.Select(l => new AddonLineSnapshot(l.Id, l.AddonId, l.NameSnapshot, l.Quantity, Money(l.UnitPriceCents),
            Enum<AddonLineStatus>(l.Status))).ToList(),
        d.Payments.Select(p => new PaymentSnapshot(p.Id, Money(p.AmountCents), Enum<PaymentMethod>(p.Method),
            p.StripePaymentIntentId, Instant(p.PaidAt), p.Note, Enum<PaymentRecordedBy>(p.RecordedBy))).ToList(),
        Instant(d.HoldExpiresAt), d.StripeCheckoutSessionId, Instant(d.RoomingWarningAcceptedAt), d.RoomingWarningText,
        Instant(d.TermsAcceptedAt), d.GroupRestrictionDeclaredNotApplicable, d.PaymentLinkTokenHash,
        d.SentNotifications.Select(n => new SentNotification(n.Key, Instant(n.SentAt))).ToList(),
        Instant(d.CreatedDate), Instant(d.ModifiedDate)));

    // ---- Boat -----------------------------------------------------------------------------------------------------

    public static BoatDocument ToDocument(BoatSnapshot s, int version) => new()
    {
        Version = version,
        Name = s.Name,
        Slug = s.Slug,
        Type = s.Type.ToString(),
        MaxNoOfGuests = s.MaxNoOfGuests,
        NoOfBeds = s.NoOfBeds,
        BeddingDescription = s.BeddingDescription,
        SecurityBondCents = Cents(s.SecurityBond),
        Rates = s.Rates.Select(r => new BoatRateDocument
        {
            SeasonId = r.SeasonId,
            PeriodType = r.PeriodType.ToString(),
            PriceCents = Cents(r.Price),
        }).ToList(),
        AllowedAddonIds = s.AllowedAddonIds.ToList(),
        IsActive = s.IsActive,
        CreatedDate = Ts(s.CreatedDate),
        ModifiedDate = Ts(s.ModifiedDate),
    };

    public static Boat ToBoat(string id, BoatDocument d) => Boat.FromSnapshot(new BoatSnapshot(
        id, d.Version, d.Name, d.Slug, Enum<BoatType>(d.Type), d.MaxNoOfGuests, d.NoOfBeds, d.BeddingDescription,
        Money(d.SecurityBondCents),
        d.Rates.Select(r => new BoatRateSnapshot(r.SeasonId, Enum<PeriodType>(r.PeriodType), Money(r.PriceCents))).ToList(),
        d.AllowedAddonIds, d.IsActive, Instant(d.CreatedDate), Instant(d.ModifiedDate)));

    // ---- Customer -------------------------------------------------------------------------------------------------

    public static CustomerDocument ToDocument(CustomerSnapshot s, int version) => new()
    {
        Version = version,
        FullName = s.FullName,
        Email = s.Email,
        MobileNumber = s.MobileNumber,
        CreatedDate = Ts(s.CreatedDate),
        ModifiedDate = Ts(s.ModifiedDate),
    };

    public static Customer ToCustomer(string id, CustomerDocument d) => Customer.FromSnapshot(new CustomerSnapshot(
        id, d.Version, d.FullName, d.Email, d.MobileNumber, Instant(d.CreatedDate), Instant(d.ModifiedDate)));

    // ---- Unavailability -------------------------------------------------------------------------------------------

    public static UnavailabilityDocument ToDocument(BoatUnavailabilitySnapshot s, int version) => new()
    {
        Version = version,
        BoatId = s.BoatId,
        FirstNight = Date(s.FirstNight),
        LastNight = Date(s.LastNight),
        Comments = s.Comments,
        CreatedDate = Ts(s.CreatedDate),
        ModifiedDate = Ts(s.ModifiedDate),
    };

    public static BoatUnavailability ToUnavailability(string id, UnavailabilityDocument d) =>
        BoatUnavailability.FromSnapshot(new BoatUnavailabilitySnapshot(id, d.Version, d.BoatId, Date(d.FirstNight),
            Date(d.LastNight), d.Comments, Instant(d.CreatedDate), Instant(d.ModifiedDate)));

    // ---- Settings aggregates --------------------------------------------------------------------------------------

    public static SeasonDocument ToDocument(SeasonSnapshot s, int version) => new()
    {
        Version = version,
        Name = s.Name,
        Ranges = s.Ranges.Select(r => new SeasonRangeDocument { StartDayMonth = r.StartDayMonth, EndDayMonth = r.EndDayMonth }).ToList(),
    };

    public static Season ToSeason(string id, SeasonDocument d) => Season.FromSnapshot(new SeasonSnapshot(id, d.Version, d.Name,
        d.Ranges.Select(r => new DayMonthRangeSnapshot(r.StartDayMonth, r.EndDayMonth)).ToList()));

    public static BlockedPeriodDocument ToDocument(BlockedPeriodSnapshot s, int version) => new()
    {
        Version = version,
        Name = s.Name,
        FirstNight = Date(s.FirstNight),
        LastNight = Date(s.LastNight),
        UsesExtendedPaymentSchedule = s.UsesExtendedPaymentSchedule,
    };

    public static BlockedPeriod ToBlockedPeriod(string id, BlockedPeriodDocument d) => BlockedPeriod.FromSnapshot(
        new BlockedPeriodSnapshot(id, d.Version, d.Name, Date(d.FirstNight), Date(d.LastNight), d.UsesExtendedPaymentSchedule));

    public static AddonDocument ToDocument(AddonDefinitionSnapshot s, int version) => new()
    {
        Version = version,
        Name = s.Name,
        Description = s.Description,
        PriceOnRequest = s.PriceOnRequest,
        MidweekPriceCents = s.MidweekPrice is { } m ? Cents(m) : null,
        WeekendPriceCents = s.WeekendPrice is { } w ? Cents(w) : null,
        WeekPriceCents = s.WeekPrice is { } k ? Cents(k) : null,
        QuantityApplies = s.QuantityApplies,
        IsActive = s.IsActive,
    };

    public static AddonDefinition ToAddon(string id, AddonDocument d) => AddonDefinition.FromSnapshot(new AddonDefinitionSnapshot(
        id, d.Version, d.Name, d.Description, d.PriceOnRequest, Money(d.MidweekPriceCents), Money(d.WeekendPriceCents),
        Money(d.WeekPriceCents), d.QuantityApplies, d.IsActive));

    public static EmailTemplateDocument ToDocument(EmailTemplateSnapshot s, int version) =>
        new() { Version = version, Subject = s.Subject, HtmlBody = s.HtmlBody };

    public static EmailTemplate ToTemplate(string id, EmailTemplateDocument d) =>
        EmailTemplate.FromSnapshot(new EmailTemplateSnapshot(Enum<EmailTemplateKey>(id), d.Version, d.Subject, d.HtmlBody));

    public static BusinessSettingsDocument ToDocument(BusinessSettingsSnapshot s, int version) => new()
    {
        Version = version,
        DepositAmountCents = Cents(s.DepositAmount),
        MinimumLeadTimeDays = s.MinimumLeadTimeDays,
        BookingsOpenUntil = Date(s.BookingsOpenUntil),
        StaffAlertEmail = s.StaffAlertEmail,
        ContactPhone = s.ContactPhone,
        ContactEmail = s.ContactEmail,
        HireTermsUrl = s.HireTermsUrl,
    };

    public static BusinessSettings ToBusinessSettings(BusinessSettingsDocument d) => BusinessSettings.FromSnapshot(
        new BusinessSettingsSnapshot(d.Version, Money(d.DepositAmountCents), d.MinimumLeadTimeDays, Date(d.BookingsOpenUntil),
            d.StaffAlertEmail, d.ContactPhone, d.ContactEmail, d.HireTermsUrl));
}
