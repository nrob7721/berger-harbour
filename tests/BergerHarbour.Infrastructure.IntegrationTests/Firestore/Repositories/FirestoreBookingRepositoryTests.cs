using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Support;
using static BergerHarbour.Infrastructure.IntegrationTests.Support.EmulatorFixture;

namespace BergerHarbour.Infrastructure.IntegrationTests.Firestore.Repositories;

[Collection(EmulatorCollection.Name)]
public class FirestoreBookingRepositoryTests(EmulatorFixture fx)
{
    private IBookingRepository Repository => fx.Get<IBookingRepository>();

    [Fact]
    public async Task A_booking_round_trips_with_every_field()
    {
        var saved = await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-01-10", "2028-01-14", email: "roundtrip@example.com",
            lines: [new StaffAddonLineInput(null, "ice", 2, 7.5m), new StaffAddonLineInput(null, "welcome-hamper", 1, null)]));
        await fx.Staff().AddManualPaymentAsync(saved.Booking.Id, new ManualPaymentRequest(1000.55m, D("2026-11-09"), "Cash", saved.Booking.Version));
        var loaded = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        Assert.Equal(saved.Booking.Reference, loaded.Reference!.Value);
        Assert.Equal(D("2028-01-10"), loaded.StartDate);
        Assert.Equal(new TimeOnly(11, 0), loaded.CheckInTime);
        Assert.Equal(4000m + 15m, loaded.TotalPrice);
        Assert.Null(loaded.AddonLines.Single(l => l.AddonId.Value == "welcome-hamper").UnitPrice);
        Assert.Equal(1000.55m, loaded.AmountPaid);
        Assert.Equal("Cash", loaded.Payments.Single().Note);
        Assert.Contains(loaded.SentNotifications, n => n.Key == "BookingConfirmed");
        Assert.True(loaded.Version >= 2);
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        var saved = await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-02-07", "2028-02-11", email: "stale@example.com"));
        var a = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        var b = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        a.RecordNotificationSent("x", fx.Clock.UtcNow);
        await Repository.SaveAsync(a);
        b.RecordNotificationSent("y", fx.Clock.UtcNow);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Repository.SaveAsync(b));
    }

    [Fact]
    public async Task Capacity_queries_count_addon_lines_across_boats()
    {
        await fx.ActivateAsync("misty-blue");
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-03-06", "2028-03-09", email: "cap1@example.com",
            lines: [new StaffAddonLineInput(null, "car-parking", 3, null)]));
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-03-08", "2028-03-13", slug: "misty-blue", email: "cap2@example.com",
            lines: [new StaffAddonLineInput(null, "car-parking", 2, null)]));
        var withParking = await Repository.ListWithAddonAsync(new AddonId("car-parking"), D("2028-03-06"), D("2028-03-09"));
        Assert.Equal(2, withParking.Count);
        var remaining = await fx.Get<AddonAvailabilityService>().GetRemainingAsync("car-parking", D("2028-03-06"), D("2028-03-09"), null);
        Assert.Equal(6, remaining.Remaining);
    }

    [Fact]
    public async Task Fleet_availability_queries_find_overlapping_bookings_of_every_boat()
    {
        await fx.ActivateAsync("misty-blue");
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-04-03", "2028-04-07", email: "fleet1@example.com"));
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-04-05", "2028-04-10", slug: "misty-blue", email: "fleet2@example.com"));
        await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-05-01", "2028-05-05", email: "fleet3@example.com"));
        var overlapping = await Repository.ListOverlappingAsync(D("2028-04-06"), D("2028-04-08"));
        Assert.Equal(2, overlapping.Count(b => b.StartDate.Month == 4 && b.StartDate.Year == 2028));
        Assert.DoesNotContain(overlapping, b => b.StartDate == D("2028-05-01"));
    }
}
