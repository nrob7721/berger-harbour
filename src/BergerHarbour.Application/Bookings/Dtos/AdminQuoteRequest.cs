using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>POST /api/bookings/quote</c>: prices for pre-filling and Recalculate from rates.</summary>
public sealed record AdminQuoteRequest(
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    IReadOnlyList<string>? AddonIds);
