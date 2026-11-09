using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests;

public static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateOnly D(string iso) => Dates.ParseIso(iso);
    public static StayRange Stay(string start, string end) => new(D(start), D(end));

    public static Season Normal() => Season.CreateNormal(Now);
    public static Season OffPeak() => Season.Rehydrate("off-peak", "Off Peak", [new SeasonRange(new DayMonth(1, 5), new DayMonth(31, 8))], 1, Now, Now);
    public static Season Peak() => Season.Rehydrate("peak", "Peak", [new SeasonRange(new DayMonth(1, 12), new DayMonth(31, 1))], 1, Now, Now);
    public static IReadOnlyList<Season> Seasons() => [Normal(), OffPeak(), Peak()];

    public static BoatDetails Details(int berth = 12, int? adultBeds = 6, int? kidsBeds = 2, string? bedding = "4 queens", string? imageUrl = null,
        decimal? deposit = null) =>
        new("Pacific Blue", "pacific-blue", BoatType.House, berth, adultBeds, kidsBeds, bedding, 2000, deposit, imageUrl, true, false);

    public static Boat Boat(bool active = true, decimal? deposit = null)
    {
        var b = Domain.Boats.Boat.Create(Details(deposit: deposit), Now);
        b.SetRates(Rates("normal", 4060, 4060, 4620, 5850).Concat(Rates("off-peak", 3570, 3570, 4050, 5250)).Concat(Rates("peak", 4500, 4500, 5100, 6200)));
        if (active) b.Activate(Seasons());
        return b;
    }

    public static IEnumerable<BoatRate> Rates(string season, decimal weekend, decimal midweek, decimal longWeekend, decimal week) =>
    [
        new(season, PeriodType.Weekend, weekend), new(season, PeriodType.Midweek, midweek),
        new(season, PeriodType.LongWeekend, longWeekend), new(season, PeriodType.Week, week),
    ];

    public static Booking StaffBooking(string start, string end, BoatId? boat = null, bool standby = false, decimal hire = 4000, decimal deposit = 1000,
        IEnumerable<AddonLine>? lines = null, string customerId = "c1", DateTimeOffset? createdAt = null, PeriodType periodType = PeriodType.Custom,
        PaymentScheduleType schedule = PaymentScheduleType.Standard)
    {
        var id = Ids.New();
        return Booking.CreateByStaff(id, "REF-" + id[..6], boat ?? new BoatId("boat-1"), periodType, Stay(start, end), new GuestNumbers(2, 0),
            SydneyTime.StandardCheckIn, SydneyTime.StandardCheckOut, standby, customerId, null, hire, deposit, schedule, lines ?? [], "hash",
            createdAt ?? Now);
    }

    public static Booking Hold(string start, string end, DateTimeOffset holdExpires, BoatId? boat = null, IEnumerable<AddonLine>? lines = null) =>
        Booking.CreateOnlineHold(boat ?? new BoatId("boat-1"), PeriodType.Midweek, Stay(start, end), new GuestNumbers(2, 0),
            new CustomerDetails("Jane", "Smith", "jane@example.com", "+61412345678"), "draft-1", 4000, 1000, PaymentScheduleType.Standard,
            lines ?? [], null, Now, holdExpires, Ids.New(), "hash", Now);

    public static AddonLine Line(string addonId, int quantity, decimal? price = null) => AddonLine.Create(null, addonId, addonId, quantity, price);
}
