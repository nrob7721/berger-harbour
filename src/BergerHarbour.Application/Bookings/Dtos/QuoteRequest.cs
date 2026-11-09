using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>POST /api/boats/{slug}/quote</c>.</summary>
public sealed record QuoteRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    IReadOnlyList<AddonRequestDto>? Addons);
