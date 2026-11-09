using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>The server quote shown on the Summary step.</summary>
public sealed record QuoteResponse(
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string SeasonName,
    decimal HirePrice,
    IReadOnlyList<QuoteAddonLine> Addons,
    decimal TotalPrice,
    bool HasPriceOnRequestAddons,
    decimal DepositAmount,
    decimal AmountDueAtCheckout,
    IReadOnlyList<ScheduleItem> PaymentSchedule,
    decimal SecurityBond,
    string? RoomingWarning);
