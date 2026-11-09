using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.BookingDrafts.Dtos;

/// <summary><c>PUT /api/booking-drafts/{draftId}</c>.</summary>
public sealed record UpdateDraftRequest(
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
    DraftStep LastStepReached);
