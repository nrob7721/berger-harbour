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

    public static Boat CompleteBoat(IReadOnlyList<Season> seasons, int maxGuests = 12, int adultBeds = 6, int kidsBeds = 2,
        decimal? deposit = null)
    {
        var boat = Boat.Create("Pacific Blue", "pacific-blue", BoatType.House, maxGuests, Now);
        boat.UpdateDetails("Pacific Blue", "pacific-blue", maxGuests, adultBeds, kidsBeds,
            "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area", 2000m, deposit, Now);
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
        DateTimeOffset? createdAt = null, TimeOnly? checkIn = null, TimeOnly? checkOut = null) =>
        Booking.CreateByStaff(NextReference(), CustomerId.New(), boatId, PeriodType.Custom, new Stay(start, end), 3, 1,
            checkIn ?? Stay.CheckInTime, checkOut ?? Stay.CheckOutTime, standby, null, hirePrice, deposit, schedule, "hash",
            createdAt ?? Now);

    public static BookingCustomerDetails Details() => new("Jane Citizen", "Jane@Example.com", "0412 345 678");

    public static Booking OnlineHold(BoatId boatId, DateOnly start, DateOnly end, DateTimeOffset holdExpiresAt,
        decimal hirePrice = 4060m, IEnumerable<RequestedAddon>? addons = null) =>
        Booking.CreateOnlineHold(NextReference(), Details(), BookingDraftId.New(), boatId, PeriodType.Midweek,
            new Stay(start, end), 3, 1, hirePrice, 1000m, PaymentSchedule.Standard, addons ?? [], holdExpiresAt, null, Now,
            "hash", Now);
}
