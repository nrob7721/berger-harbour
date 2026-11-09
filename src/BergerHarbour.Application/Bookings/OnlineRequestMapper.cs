using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings;

/// <summary>Booking app request DTOs → <see cref="BookingRequest"/>.</summary>
public static class OnlineRequestMapper
{
    public static IReadOnlyList<BookingRequestAddon> ToAddons(IReadOnlyList<AddonRequestDto>? addons) =>
        (addons ?? []).Where(a => !string.IsNullOrWhiteSpace(a.AddonId)).Select(a => new BookingRequestAddon(new AddonId(a.AddonId), a.Quantity)).ToList();

    public static BookingRequest ToRequest(QuoteRequest r) =>
        new(r.PeriodType, r.StartDate, null, r.NumberOfAdults, r.NumberOfChildren, null, null, null, null, null, null, ToAddons(r.Addons), false, null,
            false, null);

    public static BookingRequest ToRequest(CreateBookingRequest r) =>
        new(r.PeriodType, r.StartDate, null, r.NumberOfAdults, r.NumberOfChildren, r.FirstName, r.LastName, r.Email, r.Mobile, null, null,
            ToAddons(r.Addons), r.RoomingWarningAccepted, r.RoomingWarningText, r.TermsAccepted,
            string.IsNullOrWhiteSpace(r.DraftId) ? null : new Domain.BookingDrafts.BookingDraftId(r.DraftId));
}
