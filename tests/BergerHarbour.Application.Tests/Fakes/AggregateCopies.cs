using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>Deep copies through each aggregate's rehydration factory, as a database round trip would.</summary>
public static class AggregateCopies
{
    public static Boat Copy(Boat b) => Boat.Rehydrate(b.Id, new BoatDetails(b.Name, b.Slug, b.Type, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription,
        b.SecurityBond, b.DepositAmount, b.ImageUrl, b.HasSpa, b.IsPetFriendly, b.Rates.ToList(), b.AllowedAddonIds.ToList()), b.IsActive, b.CreatedDate,
        b.ModifiedDate, b.Version);

    public static Booking Copy(Booking b) => Booking.Rehydrate(new BookingState(b.Id, b.Reference, b.CreatedBy, b.IsStandby, b.Status, b.ActivatedAt,
        b.CustomerId, b.CustomerDetails, b.BookingDraftId, b.BoatId, b.PeriodType, b.Stay, b.NumberOfAdults, b.NumberOfChildren, b.CheckInTime,
        b.CheckOutTime, b.BookingComments, b.HirePrice, b.DepositAmount, b.PaymentSchedule, b.AddonLines.ToList(), b.Payments.ToList(), b.HoldExpiresAt,
        b.StripeCheckoutSessionId, b.RoomingWarningAcceptance, b.TermsAcceptedAt, b.PaymentLinkTokenHash, b.SentNotifications.ToList(), b.CreatedDate,
        b.ModifiedDate, b.Version));

    public static BookingDraft Copy(BookingDraft d) =>
        BookingDraft.Rehydrate(d.Id, d.Contents, d.LastActivityAt, d.ConvertedBookingId, d.ConvertedAt, d.AbandonedNotifiedAt, d.CreatedDate, d.Version);

    public static Customer Copy(Customer c) => Customer.Rehydrate(c.Id, c.FirstName, c.LastName, c.Email, c.MobileNumber, c.SecondaryMobileNumber,
        c.Address, c.Comments, c.CreatedDate, c.ModifiedDate, c.Version);

    public static BoatUnavailability Copy(BoatUnavailability u) =>
        BoatUnavailability.Rehydrate(u.Id, u.BoatId, u.Nights, u.Comments, u.CreatedDate, u.ModifiedDate, u.Version);

    public static Season Copy(Season s) => Season.Rehydrate(s.Id, s.Name, s.IsDefault, s.Ranges.ToList(), s.Version);

    public static BlockedPeriod Copy(BlockedPeriod p) => BlockedPeriod.Rehydrate(p.Id, p.Name, p.Nights, p.UsesExtendedPaymentSchedule, p.Version);

    public static AddonDefinition Copy(AddonDefinition a) => AddonDefinition.Rehydrate(a.Id, a.Name, a.Description, a.PriceOnRequest, a.Prices,
        a.QuantityApplies, a.Capacity, a.CapacityUnitLabel, a.SortOrder, a.IsActive, a.Version);

    public static EmailTemplate Copy(EmailTemplate t) => EmailTemplate.Rehydrate(t.Key, t.Subject, t.HtmlBody, t.Version);

    public static BusinessSettings Copy(BusinessSettings s) => BusinessSettings.Rehydrate(s.DepositAmount, s.MinimumLeadTimeDays, s.BookingsOpenUntil,
        s.StaffAlertEmail, s.ContactPhone, s.ContactEmail, s.HireTermsUrl, s.AccountName, s.Bsb, s.AccountNumber, s.Version);
}
