using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static Season Normal() => Season.CreateNormal();

    public static Season OffPeak() => Season.Create("Off Peak", [new DayMonthRange(new DayMonth(1, 5), new DayMonth(31, 8))]);

    public static Season Peak() => Season.Create("Peak", [new DayMonthRange(new DayMonth(1, 12), new DayMonth(31, 1))]);

    public static List<Season> Seasons() => [Normal(), OffPeak(), Peak()];

    public static Boat CompleteBoat(IReadOnlyList<Season> seasons, int maxGuests = 12, int beds = 8)
    {
        var boat = Boat.Create("Pacific Blue", "pacific-blue", BoatType.House, maxGuests, Now);
        boat.UpdateDetails("Pacific Blue", "pacific-blue", maxGuests, beds,
            "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area", 2000m, Now);
        var rates = new List<BoatRate>();
        foreach (var season in seasons)
        {
            var (weekend, midweek, longWeekend, week) = season.Name switch
            {
                "Off Peak" => (3570m, 3570m, 4050m, 5250m),
                "Peak" => (4500m, 4500m, 5100m, 6200m),
                _ => (4060m, 4060m, 4620m, 5850m),
            };
            rates.Add(new BoatRate(season.Id, PeriodType.Weekend, weekend));
            rates.Add(new BoatRate(season.Id, PeriodType.Midweek, midweek));
            rates.Add(new BoatRate(season.Id, PeriodType.LongWeekend, longWeekend));
            rates.Add(new BoatRate(season.Id, PeriodType.Week, week));
        }

        boat.SetRates(rates, Now);
        return boat;
    }

    private static int _sequence;

    public static BookingReference NextReference() => BookingReference.Create(2026, Interlocked.Increment(ref _sequence));

    public static Booking StaffBooking(BoatId boatId, DateOnly start, DateOnly end, bool standby = false,
        decimal hirePrice = 4060m, decimal deposit = 1000m, PaymentSchedule schedule = PaymentSchedule.Standard,
        DateTimeOffset? createdAt = null) =>
        Booking.CreateByStaff(NextReference(), CustomerId.New(), boatId, PeriodType.Custom, new Stay(start, end), 4,
            standby, null, hirePrice, deposit, schedule, "hash", createdAt ?? Now);

    public static Booking OnlineHold(BoatId boatId, DateOnly start, DateOnly end, DateTimeOffset holdExpiresAt,
        decimal hirePrice = 4060m) =>
        Booking.CreateOnlineHold(NextReference(), CustomerId.New(), boatId, PeriodType.Midweek, new Stay(start, end), 4,
            hirePrice, 1000m, PaymentSchedule.Standard, [], holdExpiresAt, null, Now, "hash", Now);
}
