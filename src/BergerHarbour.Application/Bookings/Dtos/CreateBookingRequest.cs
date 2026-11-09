using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>POST /api/bookings</c> (public): creates the PendingPayment hold and the deposit checkout.</summary>
public sealed record CreateBookingRequest(
    string DraftId,
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    string Email,
    string Mobile,
    IReadOnlyList<AddonRequestDto>? Addons,
    bool RoomingWarningAccepted,
    string? RoomingWarningText,
    bool TermsAccepted);
