using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

public sealed record AddonPricesDto(decimal Midweek, decimal Weekend, decimal Week);

public sealed record PublicAddonDto(
    string Id,
    string Name,
    string? Description,
    bool PriceOnRequest,
    bool QuantityApplies,
    AddonPricesDto? Prices);

public sealed record PublicBookingSettingsDto(
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string ContactPhone,
    string ContactEmail,
    string HireTermsUrl,
    DateOnly Today,
    string CheckInTime,
    string CheckOutTime);

/// <summary>Customer-safe boat data for the booking app.</summary>
public sealed record PublicBoatDto(
    string Slug,
    string Name,
    int MaxNoOfGuests,
    int? NoOfBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    bool IsActive,
    IReadOnlyList<PublicAddonDto> Addons,
    PublicBookingSettingsDto Settings);

/// <summary>A half-open range [Start, End) of dates the boat is taken.</summary>
public sealed record DateRangeDto(DateOnly Start, DateOnly End);

public sealed record EnquireOnlyDto(string Name, DateOnly FirstNight, DateOnly LastNight);

/// <summary>Availability ranges only — never booking or customer data.</summary>
public sealed record AvailabilityDto(IReadOnlyList<DateRangeDto> Unavailable, IReadOnlyList<EnquireOnlyDto> EnquireOnly);

public sealed record QuoteRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfGuests,
    IReadOnlyList<BookingAddonRequest>? Addons);

public sealed record QuoteAddonDto(string AddonId, string Name, int Quantity, decimal? UnitPrice, decimal? EstimatedTotal);

public sealed record MilestoneDto(MilestoneKind Kind, string Label, decimal Amount, decimal RequiredCumulative, DateOnly? DueDate);

public sealed record QuoteDto(
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    string CheckInTime,
    string CheckOutTime,
    int NumberOfGuests,
    string SeasonName,
    decimal HirePrice,
    IReadOnlyList<QuoteAddonDto> Addons,
    decimal DepositAmount,
    decimal AmountDueAtCheckout,
    bool FullPaymentAtCheckout,
    PaymentSchedule PaymentSchedule,
    IReadOnlyList<MilestoneDto> Milestones,
    string? RoomingWarningText,
    decimal SecurityBond);

public sealed record CreateOnlineBookingRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfGuests,
    string? FullName,
    string? Email,
    string? Mobile,
    IReadOnlyList<BookingAddonRequest>? Addons,
    bool RoomingWarningAccepted,
    string? RoomingWarningText,
    bool? GroupRestrictionApplies,
    bool TermsAccepted,
    string? TurnstileToken);

public sealed record CreateOnlineBookingResult(string Reference, string ClientSecret, string SessionId, decimal Amount);

public sealed record BookingStatusDto(
    string Reference,
    BookingStatus Status,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Email,
    decimal AmountPaid);
