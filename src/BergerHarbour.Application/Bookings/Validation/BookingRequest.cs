using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>A booking request as either channel sends it. Fields a channel doesn't send are empty.</summary>
public sealed record BookingRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Mobile,
    string? SecondaryMobile,
    string? Address,
    IReadOnlyList<BookingRequestAddon> Addons,
    bool RoomingWarningAccepted,
    string? RoomingWarningText,
    bool TermsAccepted,
    BookingDraftId? DraftId);
