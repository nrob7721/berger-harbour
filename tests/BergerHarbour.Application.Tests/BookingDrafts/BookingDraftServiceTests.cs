using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Application.Tests.Support;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;
using static BergerHarbour.Application.Tests.Support.ApplicationHarness;

namespace BergerHarbour.Application.Tests.BookingDrafts;

public class BookingDraftServiceTests
{
    private readonly ApplicationHarness h = new();

    [Fact]
    public async Task Turnstile_is_required()
    {
        h.Turnstile.Passes = false;
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => h.CreateDraftAsync());
        Assert.True(e.Errors.Has("turnstileToken"));
    }

    [Fact]
    public async Task A_draft_never_creates_a_customer_and_stores_the_normalised_email()
    {
        var id = await h.CreateDraftAsync(email: " Jane@Example.COM ");
        Assert.Empty(h.Db.Customers.All);
        Assert.Equal("jane@example.com", h.Db.Drafts.Get(new BookingDraftId(id))!.Contents.Email.Value);
    }

    [Fact]
    public async Task Details_are_validated()
    {
        var e = await Assert.ThrowsAsync<DomainValidationException>(() => h.Get<BookingDraftService>().CreateAsync(
            new CreateDraftRequest("pacific-blue", PeriodType.Midweek, D("2027-03-08"), 2, 0, "", "Smith", "jane@gmail", "0295551234", "t"), null));
        Assert.Equal(["email", "firstName", "mobile"], e.Errors.ByField.Keys.Order());
    }

    [Fact]
    public async Task Updating_an_unknown_or_converted_draft_is_404()
    {
        var request = new UpdateDraftRequest("pacific-blue", PeriodType.Midweek, D("2027-03-08"), 2, 0, "Jane", "Smith", "jane@example.com",
            "+61412345678", [], DraftStep.Addons);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Get<BookingDraftService>().UpdateAsync("nope", request));
        var booking = await h.BookOnlineAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => h.Get<BookingDraftService>().UpdateAsync(booking.BookingDraftId!.Value.Value, request));
    }

    [Fact]
    public async Task Reading_a_draft_returns_only_its_personal_details()
    {
        var id = await h.CreateDraftAsync(email: "Jane@Example.com");
        var details = await h.Get<BookingDraftService>().GetDetailsAsync(id);
        Assert.Equal(new DraftDetailsResponse("Jane", "Smith", "jane@example.com", "+61412345678"), details);
    }

    [Fact]
    public async Task Reading_an_unknown_or_converted_draft_is_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => h.Get<BookingDraftService>().GetDetailsAsync("nope"));
        var booking = await h.BookOnlineAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => h.Get<BookingDraftService>().GetDetailsAsync(booking.BookingDraftId!.Value.Value));
    }
}
