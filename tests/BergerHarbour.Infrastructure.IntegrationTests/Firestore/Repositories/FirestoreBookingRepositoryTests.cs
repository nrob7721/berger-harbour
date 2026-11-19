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
        var request = await fx.StaffRequestAsync("2028-01-10", "2028-01-14", email: "roundtrip@example.com",
            lines: [new StaffAddonLineInput(null, "ice", 2, 7.5m), new StaffAddonLineInput(null, "welcome-hamper", 1, null)]);
        var saved = await fx.Staff().CreateAsync(request with
        {
            SecondaryMobile = "0412 000 222", Payments = [new StaffPaymentInput(null, 1000.55m, D("2026-11-09"), "Cash")],
        });
        var loaded = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        Assert.Equal(saved.Booking.Reference, loaded.Reference!.Value);
        Assert.Equal(D("2028-01-10"), loaded.StartDate);
        Assert.Equal("+61412000222", loaded.SecondaryMobileNumber!.Value);
        Assert.Equal(new TimeOnly(11, 0), loaded.CheckInTime);
        Assert.Equal(4000m + 15m, loaded.TotalPrice);
        Assert.Null(loaded.AddonLines.Single(l => l.AddonId.Value == "welcome-hamper").UnitPrice);
        Assert.Equal(1000.55m, loaded.AmountPaid);
        Assert.Equal("Cash", loaded.Payments.Single().Note);
        Assert.Contains(loaded.SentNotifications, n => n.Key == "BookingConfirmed");
        Assert.True(loaded.Version >= 2);
    }

    [Fact]
    public async Task An_Expired_bookings_contact_details_snapshot_round_trips_with_its_address_and_comments()
    {
        var boat = await fx.BoatAsync("pacific-blue");
        var hold = Booking.CreateOnlineHold(BookingId.New(), new OnlineHold(boat.Id, Domain.BookingDrafts.BookingDraftId.New(), PeriodType.Midweek,
            StayRange.Create(D("2029-06-04"), D("2029-06-08")), 2, 0,
            new CustomerDetails("Jane", "Smith", EmailAddress.Create("snapshot@example.com"), MobileNumber.Create("0412345678")), [], 4060, 1000,
            PaymentScheduleType.Standard, null, fx.Clock.UtcNow, fx.Clock.UtcNow.AddMinutes(40), "hash-snapshot"), fx.Clock.UtcNow);
        hold.Expire(fx.Clock.UtcNow);
        var address = Domain.Customers.Address.Create("1 Wharf St", null, "Paradise Point", "QLD", "4216", "AU");
        hold.UpdateCustomerDetailsByStaff(new CustomerDetails("Janet", "Jones", EmailAddress.Create("janet@example.com"),
            MobileNumber.Create("0412000999"), address, "Likes the top deck"), fx.Clock.UtcNow);
        await Repository.SaveAsync(hold);

        var loaded = (await Repository.GetAsync(hold.Id))!.CustomerDetails!;
        Assert.Equal(("Janet", "Jones", "janet@example.com", "+61412000999"), (loaded.FirstName, loaded.LastName, loaded.Email.Value, loaded.MobileNumber.Value));
        Assert.Equal(address, loaded.Address);
        Assert.Equal("Likes the top deck", loaded.CustomerComments);
    }

    [Fact]
    public async Task Removed_stripe_payment_intent_ids_round_trip()
    {
        var saved = await fx.Staff().CreateAsync(await fx.StaffRequestAsync("2028-08-07", "2028-08-11", email: "removed@example.com"));
        var booking = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        booking.RecordPayment(new Payment("s1", 500, PaymentMethod.Stripe, "pi_removed", fx.Clock.UtcNow, null, BookingCreator.Customer), fx.Clock.UtcNow);
        booking.ChangePaymentsByStaff([], [], fx.Clock.UtcNow);
        await Repository.SaveAsync(booking);
        var loaded = (await Repository.GetAsync(new BookingId(saved.Booking.Id)))!;
        Assert.Empty(loaded.Payments);
        Assert.Equal(["pi_removed"], loaded.RemovedStripePaymentIntentIds);
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
