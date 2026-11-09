using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.BookingDrafts.Dtos;

/// <summary><c>POST /api/booking-drafts</c>.</summary>
public sealed record CreateDraftRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    string Email,
    string Mobile,
    string? TurnstileToken);
