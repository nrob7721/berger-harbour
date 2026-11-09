using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

// Firestore storage format. Money is integer cents, dates are yyyy-MM-dd strings (they sort correctly),
// times are HH:mm, instants are UTC timestamps and enums are strings.

public abstract class Doc
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("createdDate")] public DateTimeOffset CreatedDate { get; set; }
    [FirestoreProperty("modifiedDate")] public DateTimeOffset ModifiedDate { get; set; }

    protected void CopyAudit(AggregateRoot a)
    {
        Version = a.Version;
        CreatedDate = a.CreatedDate;
        ModifiedDate = a.ModifiedDate;
    }
}

internal static class Conv
{
    public static string D(DateOnly d) => Dates.Iso(d);
    public static DateOnly D(string s) => Dates.ParseIso(s);
    public static long C(decimal m) => Money.ToCents(m);
    public static long? C(decimal? m) => m is { } v ? Money.ToCents(v) : null;
    public static decimal M(long c) => Money.FromCents(c);
    public static decimal? M(long? c) => c is { } v ? Money.FromCents(v) : null;
    public static T E<T>(string s) where T : struct, Enum => Enum.Parse<T>(s);
}

[FirestoreData]
public sealed class BoatRateDoc
{
    [FirestoreProperty("seasonId")] public string SeasonId { get; set; } = "";
    [FirestoreProperty("periodType")] public string PeriodType { get; set; } = "";
    [FirestoreProperty("price")] public long Price { get; set; }
}

[FirestoreData]
public sealed class BoatDoc : Doc
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("slug")] public string Slug { get; set; } = "";
    [FirestoreProperty("type")] public string Type { get; set; } = "House";
    [FirestoreProperty("berth")] public int Berth { get; set; }
    [FirestoreProperty("adultBeds")] public int? AdultBeds { get; set; }
    [FirestoreProperty("kidsBeds")] public int? KidsBeds { get; set; }
    [FirestoreProperty("beddingDescription")] public string? BeddingDescription { get; set; }
    [FirestoreProperty("securityBond")] public long SecurityBond { get; set; }
    [FirestoreProperty("depositAmount")] public long? DepositAmount { get; set; }
    [FirestoreProperty("rates")] public List<BoatRateDoc> Rates { get; set; } = [];
    [FirestoreProperty("allowedAddonIds")] public List<string> AllowedAddonIds { get; set; } = [];
    [FirestoreProperty("imageUrl")] public string? ImageUrl { get; set; }
    [FirestoreProperty("hasSpa")] public bool HasSpa { get; set; }
    [FirestoreProperty("isPetFriendly")] public bool IsPetFriendly { get; set; }
    [FirestoreProperty("isActive")] public bool IsActive { get; set; }

    public static BoatDoc From(Boat b)
    {
        var d = new BoatDoc
        {
            Name = b.Name, Slug = b.Slug, Type = b.Type.ToString(), Berth = b.Berth, AdultBeds = b.AdultBeds, KidsBeds = b.KidsBeds,
            BeddingDescription = b.BeddingDescription, SecurityBond = Conv.C(b.SecurityBond), DepositAmount = Conv.C(b.DepositAmount),
            Rates = b.Rates.Select(r => new BoatRateDoc { SeasonId = r.SeasonId, PeriodType = r.PeriodType.ToString(), Price = Conv.C(r.Price) }).ToList(),
            AllowedAddonIds = b.AllowedAddonIds.ToList(), ImageUrl = b.ImageUrl, HasSpa = b.HasSpa, IsPetFriendly = b.IsPetFriendly, IsActive = b.IsActive,
        };
        d.CopyAudit(b);
        return d;
    }

    public Boat ToDomain(string id) => Boat.Rehydrate(new BoatId(id),
        new BoatDetails(Name, Slug, Conv.E<BoatType>(Type), Berth, AdultBeds, KidsBeds, BeddingDescription, Conv.M(SecurityBond),
            Conv.M(DepositAmount), ImageUrl, HasSpa, IsPetFriendly),
        Rates.Select(r => new BoatRate(r.SeasonId, Conv.E<PeriodType>(r.PeriodType), Conv.M(r.Price))), AllowedAddonIds, IsActive,
        Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class CustomerDoc : Doc
{
    [FirestoreProperty("firstName")] public string FirstName { get; set; } = "";
    [FirestoreProperty("lastName")] public string LastName { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("mobileNumber")] public string? MobileNumber { get; set; }
    [FirestoreProperty("secondaryMobileNumber")] public string? SecondaryMobileNumber { get; set; }
    [FirestoreProperty("address")] public string? Address { get; set; }
    [FirestoreProperty("comments")] public string? Comments { get; set; }

    public static CustomerDoc From(Customer c)
    {
        var d = new CustomerDoc
        {
            FirstName = c.FirstName, LastName = c.LastName, Email = c.Email, MobileNumber = c.MobileNumber,
            SecondaryMobileNumber = c.SecondaryMobileNumber, Address = c.Address, Comments = c.Comments,
        };
        d.CopyAudit(c);
        return d;
    }

    public Customer ToDomain(string id) =>
        Customer.Rehydrate(id, FirstName, LastName, Email, MobileNumber, SecondaryMobileNumber, Address, Comments, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class UnavailabilityDoc : Doc
{
    [FirestoreProperty("boatId")] public string BoatId { get; set; } = "";
    [FirestoreProperty("firstNight")] public string FirstNight { get; set; } = "";
    [FirestoreProperty("lastNight")] public string LastNight { get; set; } = "";
    [FirestoreProperty("comments")] public string? Comments { get; set; }

    public static UnavailabilityDoc From(BoatUnavailability u)
    {
        var d = new UnavailabilityDoc { BoatId = u.BoatId.Value, FirstNight = Conv.D(u.Nights.FirstNight), LastNight = Conv.D(u.Nights.LastNight), Comments = u.Comments };
        d.CopyAudit(u);
        return d;
    }

    public BoatUnavailability ToDomain(string id) => BoatUnavailability.Rehydrate(id, new BoatId(BoatId),
        new NightRange(Conv.D(FirstNight), Conv.D(LastNight)), Comments, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class AddonRequestDoc
{
    [FirestoreProperty("addonId")] public string AddonId { get; set; } = "";
    [FirestoreProperty("quantity")] public int Quantity { get; set; }
}

[FirestoreData]
public sealed class BookingDraftDoc : Doc
{
    [FirestoreProperty("boatId")] public string BoatId { get; set; } = "";
    [FirestoreProperty("periodType")] public string PeriodType { get; set; } = "";
    [FirestoreProperty("startDate")] public string StartDate { get; set; } = "";
    [FirestoreProperty("endDate")] public string EndDate { get; set; } = "";
    [FirestoreProperty("numberOfAdults")] public int NumberOfAdults { get; set; }
    [FirestoreProperty("numberOfChildren")] public int NumberOfChildren { get; set; }
    [FirestoreProperty("firstName")] public string FirstName { get; set; } = "";
    [FirestoreProperty("lastName")] public string LastName { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("mobileNumber")] public string MobileNumber { get; set; } = "";
    [FirestoreProperty("addonRequests")] public List<AddonRequestDoc> AddonRequests { get; set; } = [];
    [FirestoreProperty("lastStepReached")] public string LastStepReached { get; set; } = "";
    [FirestoreProperty("lastActivityAt")] public DateTimeOffset LastActivityAt { get; set; }
    [FirestoreProperty("convertedBookingId")] public string? ConvertedBookingId { get; set; }
    [FirestoreProperty("convertedAt")] public DateTimeOffset? ConvertedAt { get; set; }
    [FirestoreProperty("abandonedNotifiedAt")] public DateTimeOffset? AbandonedNotifiedAt { get; set; }

    public static BookingDraftDoc From(BookingDraft d)
    {
        var doc = new BookingDraftDoc
        {
            BoatId = d.BoatId.Value, PeriodType = d.PeriodType.ToString(), StartDate = Conv.D(d.Stay.Start), EndDate = Conv.D(d.Stay.End),
            NumberOfAdults = d.Guests.Adults, NumberOfChildren = d.Guests.Children, FirstName = d.FirstName, LastName = d.LastName,
            Email = d.Email, MobileNumber = d.MobileNumber,
            AddonRequests = d.AddonRequests.Select(a => new AddonRequestDoc { AddonId = a.AddonId, Quantity = a.Quantity }).ToList(),
            LastStepReached = d.LastStepReached.ToString(), LastActivityAt = d.LastActivityAt, ConvertedBookingId = d.ConvertedBookingId,
            ConvertedAt = d.ConvertedAt, AbandonedNotifiedAt = d.AbandonedNotifiedAt,
        };
        doc.CopyAudit(d);
        return doc;
    }

    public BookingDraft ToDomain(string id) => BookingDraft.Rehydrate(id,
        new DraftDetails(new BoatId(BoatId), Conv.E<PeriodType>(PeriodType), new StayRange(Conv.D(StartDate), Conv.D(EndDate)),
            new GuestNumbers(NumberOfAdults, NumberOfChildren), FirstName, LastName, Email, MobileNumber,
            AddonRequests.Select(a => new AddonRequest(a.AddonId, a.Quantity)).ToList(), Conv.E<DraftStep>(LastStepReached)),
        LastActivityAt, ConvertedBookingId, ConvertedAt, AbandonedNotifiedAt, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class CustomerDetailsDoc
{
    [FirestoreProperty("firstName")] public string FirstName { get; set; } = "";
    [FirestoreProperty("lastName")] public string LastName { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("mobileNumber")] public string MobileNumber { get; set; } = "";
}

[FirestoreData]
public sealed class AddonLineDoc
{
    [FirestoreProperty("id")] public string Id { get; set; } = "";
    [FirestoreProperty("addonId")] public string AddonId { get; set; } = "";
    [FirestoreProperty("nameSnapshot")] public string NameSnapshot { get; set; } = "";
    [FirestoreProperty("quantity")] public int Quantity { get; set; }
    [FirestoreProperty("unitPrice")] public long? UnitPrice { get; set; }
}

[FirestoreData]
public sealed class PaymentDoc
{
    [FirestoreProperty("id")] public string Id { get; set; } = "";
    [FirestoreProperty("amount")] public long Amount { get; set; }
    [FirestoreProperty("method")] public string Method { get; set; } = "";
    [FirestoreProperty("stripePaymentIntentId")] public string? StripePaymentIntentId { get; set; }
    [FirestoreProperty("paidAt")] public DateTimeOffset PaidAt { get; set; }
    [FirestoreProperty("note")] public string? Note { get; set; }
    [FirestoreProperty("recordedBy")] public string RecordedBy { get; set; } = "";
}

[FirestoreData]
public sealed class RoomingDoc
{
    [FirestoreProperty("acceptedAt")] public DateTimeOffset AcceptedAt { get; set; }
    [FirestoreProperty("warningTextShown")] public string WarningTextShown { get; set; } = "";
}

[FirestoreData]
public sealed class SentNotificationDoc
{
    [FirestoreProperty("key")] public string Key { get; set; } = "";
    [FirestoreProperty("sentAt")] public DateTimeOffset SentAt { get; set; }
}

[FirestoreData]
public sealed class BookingDoc : Doc
{
    [FirestoreProperty("reference")] public string? Reference { get; set; }
    [FirestoreProperty("createdBy")] public string CreatedBy { get; set; } = "";
    [FirestoreProperty("isStandby")] public bool IsStandby { get; set; }
    [FirestoreProperty("status")] public string Status { get; set; } = "";
    [FirestoreProperty("activatedAt")] public DateTimeOffset? ActivatedAt { get; set; }
    [FirestoreProperty("customerId")] public string? CustomerId { get; set; }
    [FirestoreProperty("customerDetails")] public CustomerDetailsDoc? CustomerDetails { get; set; }
    [FirestoreProperty("bookingDraftId")] public string? BookingDraftId { get; set; }
    [FirestoreProperty("boatId")] public string BoatId { get; set; } = "";
    [FirestoreProperty("periodType")] public string PeriodType { get; set; } = "";
    [FirestoreProperty("startDate")] public string StartDate { get; set; } = "";
    [FirestoreProperty("endDate")] public string EndDate { get; set; } = "";
    [FirestoreProperty("numberOfAdults")] public int NumberOfAdults { get; set; }
    [FirestoreProperty("numberOfChildren")] public int NumberOfChildren { get; set; }
    [FirestoreProperty("checkInTime")] public string CheckInTime { get; set; } = "11:00";
    [FirestoreProperty("checkOutTime")] public string CheckOutTime { get; set; } = "08:00";
    [FirestoreProperty("bookingComments")] public string? BookingComments { get; set; }
    [FirestoreProperty("hirePrice")] public long HirePrice { get; set; }
    [FirestoreProperty("depositAmount")] public long DepositAmount { get; set; }
    [FirestoreProperty("paymentSchedule")] public string PaymentSchedule { get; set; } = "";
    [FirestoreProperty("addonLines")] public List<AddonLineDoc> AddonLines { get; set; } = [];
    /// <summary>Denormalised for capacity queries (array-contains).</summary>
    [FirestoreProperty("addonIds")] public List<string> AddonIds { get; set; } = [];
    [FirestoreProperty("payments")] public List<PaymentDoc> Payments { get; set; } = [];
    [FirestoreProperty("holdExpiresAt")] public DateTimeOffset? HoldExpiresAt { get; set; }
    [FirestoreProperty("stripeCheckoutSessionId")] public string? StripeCheckoutSessionId { get; set; }
    [FirestoreProperty("roomingWarningAcceptance")] public RoomingDoc? RoomingWarningAcceptance { get; set; }
    [FirestoreProperty("termsAcceptedAt")] public DateTimeOffset? TermsAcceptedAt { get; set; }
    [FirestoreProperty("paymentLinkTokenHash")] public string PaymentLinkTokenHash { get; set; } = "";
    [FirestoreProperty("sentNotifications")] public List<SentNotificationDoc> SentNotifications { get; set; } = [];

    public static BookingDoc From(Booking b)
    {
        var d = new BookingDoc
        {
            Reference = b.Reference, CreatedBy = b.CreatedBy.ToString(), IsStandby = b.IsStandby, Status = b.Status.ToString(),
            ActivatedAt = b.ActivatedAt, CustomerId = b.CustomerId,
            CustomerDetails = b.CustomerDetails is { } c
                ? new CustomerDetailsDoc { FirstName = c.FirstName, LastName = c.LastName, Email = c.Email, MobileNumber = c.MobileNumber }
                : null,
            BookingDraftId = b.BookingDraftId, BoatId = b.BoatId.Value, PeriodType = b.PeriodType.ToString(),
            StartDate = Conv.D(b.StartDate), EndDate = Conv.D(b.EndDate), NumberOfAdults = b.Guests.Adults, NumberOfChildren = b.Guests.Children,
            CheckInTime = Dates.FormatTimeOfDay(b.CheckInTime), CheckOutTime = Dates.FormatTimeOfDay(b.CheckOutTime),
            BookingComments = b.BookingComments, HirePrice = Conv.C(b.HirePrice), DepositAmount = Conv.C(b.DepositAmount),
            PaymentSchedule = b.PaymentSchedule.ToString(),
            AddonLines = b.AddonLines.Select(l => new AddonLineDoc { Id = l.Id, AddonId = l.AddonId, NameSnapshot = l.NameSnapshot, Quantity = l.Quantity, UnitPrice = Conv.C(l.UnitPrice) }).ToList(),
            AddonIds = b.AddonIds.ToList(),
            Payments = b.Payments.Select(p => new PaymentDoc
            {
                Id = p.Id, Amount = Conv.C(p.Amount), Method = p.Method.ToString(), StripePaymentIntentId = p.StripePaymentIntentId,
                PaidAt = p.PaidAt, Note = p.Note, RecordedBy = p.RecordedBy.ToString(),
            }).ToList(),
            HoldExpiresAt = b.HoldExpiresAt, StripeCheckoutSessionId = b.StripeCheckoutSessionId,
            RoomingWarningAcceptance = b.RoomingWarningAcceptance is { } r ? new RoomingDoc { AcceptedAt = r.AcceptedAt, WarningTextShown = r.WarningTextShown } : null,
            TermsAcceptedAt = b.TermsAcceptedAt, PaymentLinkTokenHash = b.PaymentLinkTokenHash,
            SentNotifications = b.SentNotifications.Select(n => new SentNotificationDoc { Key = n.Key, SentAt = n.SentAt }).ToList(),
        };
        d.CopyAudit(b);
        return d;
    }

    public Booking ToDomain(string id) => Booking.Rehydrate(new Booking.State(
        id, Reference, Conv.E<BookingCreator>(CreatedBy), IsStandby, Conv.E<BookingStatus>(Status), ActivatedAt, CustomerId,
        CustomerDetails is { } c ? new Domain.Bookings.CustomerDetails(c.FirstName, c.LastName, c.Email, c.MobileNumber) : null,
        BookingDraftId, new BoatId(BoatId), Conv.E<PeriodType>(PeriodType), new StayRange(Conv.D(StartDate), Conv.D(EndDate)),
        new GuestNumbers(NumberOfAdults, NumberOfChildren), Dates.ParseTime(CheckInTime), Dates.ParseTime(CheckOutTime), BookingComments,
        Conv.M(HirePrice), Conv.M(DepositAmount), Conv.E<PaymentScheduleType>(PaymentSchedule),
        AddonLines.Select(l => new AddonLine(l.Id, l.AddonId, l.NameSnapshot, l.Quantity, Conv.M(l.UnitPrice))).ToList(),
        Payments.Select(p => new Payment(p.Id, Conv.M(p.Amount), Conv.E<PaymentMethod>(p.Method), p.StripePaymentIntentId, p.PaidAt, p.Note,
            Conv.E<BookingCreator>(p.RecordedBy))).ToList(),
        HoldExpiresAt, StripeCheckoutSessionId,
        RoomingWarningAcceptance is { } r ? new Domain.Bookings.RoomingWarningAcceptance(r.AcceptedAt, r.WarningTextShown) : null,
        TermsAcceptedAt, PaymentLinkTokenHash, SentNotifications.Select(n => new SentNotification(n.Key, n.SentAt)).ToList(),
        Version, CreatedDate, ModifiedDate));
}

[FirestoreData]
public sealed class SeasonRangeDoc
{
    [FirestoreProperty("start")] public string Start { get; set; } = "";
    [FirestoreProperty("end")] public string End { get; set; } = "";
}

[FirestoreData]
public sealed class SeasonDoc : Doc
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("ranges")] public List<SeasonRangeDoc> Ranges { get; set; } = [];

    public static SeasonDoc From(Season s)
    {
        var d = new SeasonDoc { Name = s.Name, Ranges = s.Ranges.Select(r => new SeasonRangeDoc { Start = r.Start.ToString(), End = r.End.ToString() }).ToList() };
        d.CopyAudit(s);
        return d;
    }

    public Season ToDomain(string id) => Season.Rehydrate(id, Name,
        Ranges.Select(r => new SeasonRange(DayMonth.Parse(r.Start, "ranges"), DayMonth.Parse(r.End, "ranges"))), Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class BlockedPeriodDoc : Doc
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("firstNight")] public string FirstNight { get; set; } = "";
    [FirestoreProperty("lastNight")] public string LastNight { get; set; } = "";
    [FirestoreProperty("usesExtendedPaymentSchedule")] public bool UsesExtendedPaymentSchedule { get; set; }

    public static BlockedPeriodDoc From(BlockedPeriod p)
    {
        var d = new BlockedPeriodDoc
        {
            Name = p.Name, FirstNight = Conv.D(p.Nights.FirstNight), LastNight = Conv.D(p.Nights.LastNight),
            UsesExtendedPaymentSchedule = p.UsesExtendedPaymentSchedule,
        };
        d.CopyAudit(p);
        return d;
    }

    public BlockedPeriod ToDomain(string id) => BlockedPeriod.Rehydrate(id, Name, new NightRange(Conv.D(FirstNight), Conv.D(LastNight)),
        UsesExtendedPaymentSchedule, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class AddonDoc : Doc
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("description")] public string? Description { get; set; }
    [FirestoreProperty("priceOnRequest")] public bool PriceOnRequest { get; set; }
    [FirestoreProperty("midweekPrice")] public long? MidweekPrice { get; set; }
    [FirestoreProperty("weekendPrice")] public long? WeekendPrice { get; set; }
    [FirestoreProperty("weekPrice")] public long? WeekPrice { get; set; }
    [FirestoreProperty("quantityApplies")] public bool QuantityApplies { get; set; }
    [FirestoreProperty("capacity")] public int? Capacity { get; set; }
    [FirestoreProperty("capacityUnitLabel")] public string? CapacityUnitLabel { get; set; }
    [FirestoreProperty("sortOrder")] public int SortOrder { get; set; }
    [FirestoreProperty("isActive")] public bool IsActive { get; set; }

    public static AddonDoc From(AddonDefinition a)
    {
        var d = new AddonDoc
        {
            Name = a.Name, Description = a.Description, PriceOnRequest = a.PriceOnRequest,
            MidweekPrice = Conv.C(a.Prices?.Midweek), WeekendPrice = Conv.C(a.Prices?.Weekend), WeekPrice = Conv.C(a.Prices?.Week),
            QuantityApplies = a.QuantityApplies, Capacity = a.Capacity, CapacityUnitLabel = a.CapacityUnitLabel, SortOrder = a.SortOrder, IsActive = a.IsActive,
        };
        d.CopyAudit(a);
        return d;
    }

    public AddonDefinition ToDomain(string id) => AddonDefinition.Rehydrate(id, Name, Description, PriceOnRequest,
        MidweekPrice is { } m && WeekendPrice is { } we && WeekPrice is { } w ? new AddonPrices(Conv.M(m), Conv.M(we), Conv.M(w)) : null,
        QuantityApplies, Capacity, CapacityUnitLabel, SortOrder, IsActive, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class EmailTemplateDoc : Doc
{
    [FirestoreProperty("subject")] public string Subject { get; set; } = "";
    [FirestoreProperty("htmlBody")] public string HtmlBody { get; set; } = "";

    public static EmailTemplateDoc From(EmailTemplate t)
    {
        var d = new EmailTemplateDoc { Subject = t.Subject, HtmlBody = t.HtmlBody };
        d.CopyAudit(t);
        return d;
    }

    public EmailTemplate ToDomain(string key) =>
        EmailTemplate.Rehydrate(Conv.E<EmailTemplateKey>(key), Subject, HtmlBody, Version, CreatedDate, ModifiedDate);
}

[FirestoreData]
public sealed class BusinessSettingsDoc : Doc
{
    [FirestoreProperty("depositAmount")] public long DepositAmount { get; set; }
    [FirestoreProperty("minimumLeadTimeDays")] public int MinimumLeadTimeDays { get; set; }
    [FirestoreProperty("bookingsOpenUntil")] public string BookingsOpenUntil { get; set; } = "";
    [FirestoreProperty("staffAlertEmail")] public string? StaffAlertEmail { get; set; }
    [FirestoreProperty("contactPhone")] public string? ContactPhone { get; set; }
    [FirestoreProperty("contactEmail")] public string? ContactEmail { get; set; }
    [FirestoreProperty("hireTermsUrl")] public string? HireTermsUrl { get; set; }
    [FirestoreProperty("accountName")] public string? AccountName { get; set; }
    [FirestoreProperty("bsb")] public string? Bsb { get; set; }
    [FirestoreProperty("accountNumber")] public string? AccountNumber { get; set; }

    public static BusinessSettingsDoc From(BusinessSettings s)
    {
        var d = new BusinessSettingsDoc
        {
            DepositAmount = Conv.C(s.DepositAmount), MinimumLeadTimeDays = s.MinimumLeadTimeDays, BookingsOpenUntil = Conv.D(s.BookingsOpenUntil),
            StaffAlertEmail = s.StaffAlertEmail, ContactPhone = s.ContactPhone, ContactEmail = s.ContactEmail, HireTermsUrl = s.HireTermsUrl,
            AccountName = s.AccountName, Bsb = s.Bsb, AccountNumber = s.AccountNumber,
        };
        d.CopyAudit(s);
        return d;
    }

    public BusinessSettings ToDomain() => BusinessSettings.Rehydrate(new BusinessSettingsDetails(Conv.M(DepositAmount), MinimumLeadTimeDays,
        Conv.D(BookingsOpenUntil), StaffAlertEmail, ContactPhone, ContactEmail, HireTermsUrl, AccountName, Bsb, AccountNumber),
        Version, CreatedDate, ModifiedDate);
}
