using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Domain.Tests.Support.TestDates;

namespace BergerHarbour.Domain.Tests.Support;

/// <summary>Builds bookings for domain tests.</summary>
public static class TestBookings
{
    public static readonly BoatId PacificBlue = new("pacific-blue");
    public static readonly BoatId MistyBlue = new("misty-blue");
    public static readonly DateTimeOffset Now = At("2026-11-09T00:00:00Z");

    public static Booking Staff(
        string start, string end, BoatId? boat = null, bool standby = false, decimal hire = 4000, decimal deposit = 1000,
        IReadOnlyList<AddonLine>? lines = null, string lastName = "SMITH", int suffix = 0, CustomerId? customer = null, DateTimeOffset? now = null,
        PeriodType type = PeriodType.Custom, PaymentScheduleType schedule = PaymentScheduleType.Standard, TimeOnly? checkIn = null, TimeOnly? checkOut = null)
    {
        var stay = StayRange.Create(D(start), D(end));
        var reference = BookingReference.Create(BookingReference.PrefixFor(lastName, stay.Start), suffix);
        return Booking.CreateByStaff(BookingId.New(), reference, customer ?? new CustomerId("c1"),
            new StaffBookingDetails(boat ?? PacificBlue, type, stay, 2, 0, checkIn ?? Sydney.StandardCheckIn, checkOut ?? Sydney.StandardCheckOut, standby,
                null, hire, schedule, lines ?? []),
            deposit, "hash", now ?? Now);
    }

    public static Booking Hold(string start, string end, DateTimeOffset expiresAt, BoatId? boat = null, IReadOnlyList<AddonLine>? lines = null, decimal hire = 4060)
    {
        var details = new CustomerDetails("Jane", "Smith", EmailAddress.Create("jane@example.com"), MobileNumber.Create("+61412345678"));
        return Booking.CreateOnlineHold(BookingId.New(), new OnlineHold(boat ?? PacificBlue, BookingDraftId.New(), PeriodType.Midweek,
            StayRange.Create(D(start), D(end)), 2, 0, details, lines ?? [], hire, 1000, PaymentScheduleType.Standard, null, Now, expiresAt, "hash"), Now);
    }

    public static AddonLine Line(string addonId, int quantity, decimal? unitPrice = null) =>
        AddonLine.Create(null, new AddonId(addonId), addonId, quantity, unitPrice);

    public static Payment Paid(decimal amount, string? intent = null) =>
        new(Guid.NewGuid().ToString("N"), amount, intent is null ? PaymentMethod.Manual : PaymentMethod.Stripe, intent, Now, null, BookingCreator.SystemUser);
}
