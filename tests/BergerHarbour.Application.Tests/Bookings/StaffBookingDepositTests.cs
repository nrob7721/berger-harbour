using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Boats.Dtos;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Settings;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.Bookings;

/// <summary>The booking's deposit follows its boat on staff saves until a payment is kept (§7.3).</summary>
public class StaffBookingDepositTests
{
    private readonly ApplicationHarness h = new();

    private async Task SetBoatDepositAsync(string slug, decimal? deposit)
    {
        var service = h.Get<BoatService>();
        var b = await service.GetAsync(h.Boat(slug).Id.Value);
        await service.UpdateAsync(b.Id, new SaveBoatRequest(b.Name, b.Slug, b.Berth, b.AdultBeds, b.KidsBeds, b.BeddingDescription, b.SecurityBond,
            deposit, b.Images, b.Tags, b.HasSpa, b.IsPetFriendly, b.IsActive, b.Rates, b.AllowedAddonIds, b.Version));
    }

    private async Task SetDefaultDepositAsync(decimal deposit)
    {
        var service = h.Get<BusinessSettingsService>();
        await service.UpdateAsync((await service.GetAsync()) with { DepositAmount = deposit });
    }

    private async Task<AdminBookingDetail> CreateAsync() => (await h.Staff.CreateAsync(h.StaffRequest("2027-01-15", "2027-01-19"))).Booking;

    private async Task<AdminBookingDetail> SaveAsync(AdminBookingDetail d, Func<UpdateStaffBookingRequest, UpdateStaffBookingRequest>? change = null) =>
        (await h.Staff.UpdateAsync(d.Id, UpdateRequest(d, change))).Booking;

    [Fact]
    public async Task A_booking_without_payments_takes_its_new_boats_deposit()
    {
        await SetBoatDepositAsync("ocean-spirit", 1500);
        h.Activate("ocean-spirit");
        var created = await CreateAsync();
        Assert.Equal(1000m, created.DepositAmount);

        var moved = await SaveAsync(created, r => r with { BoatId = h.Boat("ocean-spirit").Id.Value });
        Assert.Equal(1500m, moved.DepositAmount);
    }

    [Fact]
    public async Task A_booking_without_payments_takes_the_current_deposit_after_a_change_to_the_boat_or_the_Settings_default()
    {
        var created = await CreateAsync();

        await SetDefaultDepositAsync(800);
        var afterDefault = await SaveAsync(created);
        Assert.Equal(800m, afterDefault.DepositAmount);

        await SetBoatDepositAsync("pacific-blue", 1200);
        Assert.Equal(1200m, (await SaveAsync(afterDefault)).DepositAmount);
    }

    [Fact]
    public async Task A_save_keeping_an_existing_payment_leaves_the_deposit_unchanged_even_with_a_new_boat()
    {
        await SetBoatDepositAsync("ocean-spirit", 1500);
        h.Activate("ocean-spirit");
        var paid = await SaveAsync(await CreateAsync(), r => r with { Payments = [NewPayment(200, "2026-11-09")] });

        await SetBoatDepositAsync("pacific-blue", 1200);
        Assert.Equal(1000m, (await SaveAsync(paid)).DepositAmount);
        var moved = await SaveAsync(await h.Staff.GetAsync(paid.Id), r => r with { BoatId = h.Boat("ocean-spirit").Id.Value });
        Assert.Equal(1000m, moved.DepositAmount);
    }

    [Fact]
    public async Task A_boat_change_saved_together_with_the_first_payment_sets_the_new_boats_deposit_and_the_payment_then_fixes_it()
    {
        await SetBoatDepositAsync("ocean-spirit", 1500);
        h.Activate("ocean-spirit");
        var moved = await SaveAsync(await CreateAsync(), r => r with
        {
            BoatId = h.Boat("ocean-spirit").Id.Value, Payments = [NewPayment(200, "2026-11-09")],
        });
        Assert.Equal(1500m, moved.DepositAmount);

        await SetBoatDepositAsync("ocean-spirit", 900);
        Assert.Equal(1500m, (await SaveAsync(moved)).DepositAmount);
    }

    [Fact]
    public async Task A_save_removing_every_payment_sets_the_boats_deposit_again()
    {
        var paid = await SaveAsync(await CreateAsync(), r => r with { Payments = [NewPayment(200, "2026-11-09")] });
        await SetBoatDepositAsync("pacific-blue", 1200);

        var cleared = await SaveAsync(paid, r => r with { Payments = [] });
        Assert.Empty(cleared.Payments);
        Assert.Equal(1200m, cleared.DepositAmount);
    }

    [Fact]
    public async Task A_staff_confirmation_without_payments_before_takes_the_boats_current_deposit()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromDays(1));
        await SetBoatDepositAsync("pacific-blue", 1200);
        var expired = await h.Staff.GetAsync(created.BookingId);
        Assert.Equal(BookingStatus.Expired, expired.Status);
        Assert.Equal(1000m, expired.DepositAmount);

        // The threshold is min(1200, 4060) with the deposit the save stores, so 1000 isn't enough any more.
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => h.SaveWithPaymentAsync(expired, 1000, "2026-11-09"));
        Assert.Equal("To activate this booking, record at least $1,200.00.", e.Errors.ByField["payments"][0]);

        var confirmed = await h.SaveWithPaymentAsync(expired, 1200, "2026-11-09");
        Assert.Equal(BookingStatus.Active, confirmed.Status);
        Assert.Equal(1200m, confirmed.DepositAmount);
    }

    [Fact]
    public async Task A_staff_confirmation_keeping_a_payment_keeps_the_deposit()
    {
        var created = await h.Online.CreateAsync(BookingRequest(await h.CreateDraftAsync()));
        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.Staff.CreateAsync(h.StaffRequest("2027-03-08", "2027-03-12", email: "other@example.com", lastName: "Jones"));
        await h.PayDepositAsync(created.BookingId);
        await SetBoatDepositAsync("pacific-blue", 1200);
        var paidButExpired = await h.Staff.GetAsync(created.BookingId);

        var moved = await h.Staff.UpdateAsync(paidButExpired.Id, UpdateRequest(paidButExpired, r => r with
        {
            StartDate = D("2027-04-05"), EndDate = D("2027-04-09"),
        }));
        Assert.Equal(BookingStatus.Active, moved.Booking.Status);
        Assert.Equal(1000m, moved.Booking.DepositAmount);
    }
}
