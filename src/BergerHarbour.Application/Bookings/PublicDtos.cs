using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

public sealed record AddonPricesDto(decimal Midweek, decimal Weekend, decimal Week);

/// <param name="HasCapacity">When true, ask GET /api/addons/{id}/availability for what is left; hide it at 0.</param>
public sealed record PublicAddonDto(
    string Id,
    string Name,
    string? Description,
    bool PriceOnRequest,
    bool QuantityApplies,
    AddonPricesDto? Prices,
    bool HasCapacity,
    string? CapacityUnitLabel);

/// <param name="CheckInTime">Start of the check-in window, e.g. "11am".</param>
/// <param name="CheckInWindowEnd">End of the check-in window, e.g. "2pm".</param>
/// <param name="CheckOutTime">e.g. "8am".</param>
public sealed record PublicBookingSettingsDto(
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string ContactPhone,
    string ContactEmail,
    string HireTermsUrl,
    DateOnly Today,
    string CheckInTime,
    string CheckInWindowEnd,
    string CheckOutTime);

/// <summary>An active house boat on the booking app's "Dates &amp; boat" step.</summary>
public sealed record PublicFleetBoatDto(
    string Slug,
    string Name,
    string? ImageUrl,
    int MaxNoOfGuests,
    bool HasSpa,
    bool IsPetFriendly);

/// <summary>Every active house boat, largest first then by name, plus the booking settings. Also the warm-up call.</summary>
public sealed record PublicFleetDto(IReadOnlyList<PublicFleetBoatDto> Boats, PublicBookingSettingsDto Settings);

/// <summary>Customer-safe data for one boat, active or not. Add-ons are in the admin-defined order.</summary>
public sealed record PublicBoatDto(
    string Slug,
    string Name,
    string? ImageUrl,
    int MaxNoOfGuests,
    bool HasSpa,
    bool IsPetFriendly,
    int? AdultBeds,
    int? KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal DepositAmount,
    bool IsActive,
    IReadOnlyList<PublicAddonDto> Addons);

/// <summary>A half-open range [Start, End) of dates the boat is taken.</summary>
public sealed record DateRangeDto(DateOnly Start, DateOnly End);

public sealed record EnquireOnlyDto(string Name, DateOnly FirstNight, DateOnly LastNight);

/// <summary>The ranges one boat is taken: its blocking bookings and unavailabilities.</summary>
public sealed record BoatAvailabilityDto(string Slug, IReadOnlyList<DateRangeDto> Unavailable);

/// <summary>Availability ranges of every active house boat — never booking or customer data.</summary>
public sealed record FleetAvailabilityDto(IReadOnlyList<EnquireOnlyDto> EnquireOnly, IReadOnlyList<BoatAvailabilityDto> Boats);

/// <param name="HirePrice">Null when the boat has no rate for the stay, which then cannot be booked online.</param>
public sealed record BoatPeriodDto(string Slug, bool Available, decimal? HirePrice);

/// <summary>Each active house boat's availability and hire price for one stay.</summary>
public sealed record PeriodAvailabilityDto(IReadOnlyList<BoatPeriodDto> Boats);

/// <summary>What is left of a capacity-limited add-on for a stay, across all boats.</summary>
public sealed record PublicAddonAvailabilityDto(int Remaining);

public sealed record CreateBookingDraftRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? FullName,
    string? Email,
    string? Mobile,
    string? TurnstileToken);

public sealed record BookingDraftCreatedDto(string DraftId);

public sealed record UpdateBookingDraftRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? FullName,
    string? Email,
    string? Mobile,
    IReadOnlyList<BookingAddonRequest>? Addons,
    BookingStep LastStepReached);

public sealed record QuoteRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    IReadOnlyList<BookingAddonRequest>? Addons);

public sealed record QuoteAddonDto(string AddonId, string Name, int Quantity, decimal? UnitPrice, decimal? LineTotal);

/// <param name="DueNow">The milestone's due date has already passed (the payment link is emailed with the confirmation).</param>
public sealed record MilestoneDto(
    MilestoneKind Kind,
    string Label,
    decimal Amount,
    decimal RequiredCumulative,
    DateOnly? DueDate,
    bool DueNow);

/// <param name="TotalPrice">Hire price plus add-ons with a price; price-on-request add-ons are added once priced.</param>
public sealed record QuoteDto(
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    string CheckInTime,
    string CheckInWindowEnd,
    string CheckOutTime,
    int NumberOfAdults,
    int NumberOfChildren,
    string SeasonName,
    decimal HirePrice,
    IReadOnlyList<QuoteAddonDto> Addons,
    decimal TotalPrice,
    decimal DepositAmount,
    decimal AmountDueAtCheckout,
    PaymentSchedule PaymentSchedule,
    IReadOnlyList<MilestoneDto> Milestones,
    string? RoomingWarningText,
    decimal SecurityBond);

public sealed record CreateOnlineBookingRequest(
    string? DraftId,
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? FullName,
    string? Email,
    string? Mobile,
    IReadOnlyList<BookingAddonRequest>? Addons,
    bool RoomingWarningAccepted,
    string? RoomingWarningText,
    bool TermsAccepted);

public sealed record CreateOnlineBookingResult(string Reference, string ClientSecret, string SessionId, decimal Amount);

public sealed record BookingStatusDto(
    string Reference,
    BookingStatus Status,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Email,
    decimal AmountPaid);
