using BergerHarbour.Application.Customers;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

public sealed record TimelineBoatDto(string Id, string Name, int MaxNoOfGuests, bool IsActive);

public sealed record TimelineBookingDto(
    string Id,
    string Reference,
    string BoatId,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType,
    BookingStatus Status,
    bool IsStandby,
    PaymentStatus PaymentStatus,
    string CustomerName);

public sealed record TimelineUnavailabilityDto(string Id, string BoatId, DateOnly FirstNight, DateOnly LastNight, string? Comments);

public sealed record TimelineBlockedPeriodDto(string Id, string Name, DateOnly FirstNight, DateOnly LastNight);

/// <summary>Boats are ordered by max guests (largest first), then name.</summary>
public sealed record TimelineDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TimelineBoatDto> Boats,
    IReadOnlyList<TimelineBookingDto> Bookings,
    IReadOnlyList<TimelineUnavailabilityDto> Unavailabilities,
    IReadOnlyList<TimelineBlockedPeriodDto> BlockedPeriods);

public sealed record BookingSearchResultDto(
    string Id,
    string Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    BookingStatus Status,
    bool IsStandby,
    string CustomerName,
    string CustomerEmail);

/// <param name="Remaining">For capacity-limited add-ons: what is left for the booking's dates, excluding this booking.</param>
public sealed record AddonLineDto(
    string Id,
    string AddonId,
    string Name,
    int Quantity,
    decimal? UnitPrice,
    decimal? LineTotal,
    int? Remaining,
    int? Capacity);

public sealed record PaymentDto(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    PaymentRecordedBy RecordedBy);

/// <summary>What the customer entered online (shown for bookings that have no customer record yet).</summary>
public sealed record BookingCustomerDetailsDto(string FullName, string Email, string MobileNumber);

/// <param name="Customer">Null for online bookings that never became Active; see <paramref name="CustomerDetails"/>.</param>
/// <param name="Warnings">Non-blocking: guests above max or beds, add-on capacity exceeded, changeover time clashes.</param>
public sealed record BookingDetailDto(
    string Id,
    int Version,
    string Reference,
    BookingCreatedBy CreatedBy,
    bool IsStandby,
    BookingStatus Status,
    string BoatId,
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    int NumberOfGuests,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    string? Comments,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentSchedule PaymentSchedule,
    CustomerDto? Customer,
    BookingCustomerDetailsDto? CustomerDetails,
    IReadOnlyList<AddonLineDto> AddonLines,
    IReadOnlyList<PaymentDto> Payments,
    decimal TotalPrice,
    decimal AmountPaid,
    decimal AmountOwing,
    decimal AmountDueNow,
    PaymentStatus PaymentStatus,
    IReadOnlyList<MilestoneDto> Milestones,
    DateTimeOffset? HoldExpiresAt,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    DateTimeOffset? TermsAcceptedAt,
    DateTimeOffset? RoomingWarningAcceptedAt,
    string? RoomingWarningText,
    IReadOnlyList<SentNotification> SentNotifications,
    IReadOnlyList<string> Warnings);

/// <summary>An add-on line for a new staff booking (e.g. copied by Re-book). A null unit price uses the current price.</summary>
public sealed record AdminAddonLineRequest(string AddonId, int Quantity, decimal? UnitPrice);

/// <param name="CheckInTime">Defaults to 11:00.</param>
/// <param name="CheckOutTime">Defaults to 08:00.</param>
public sealed record AdminBookingRequest(
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly? CheckInTime,
    TimeOnly? CheckOutTime,
    string? Email,
    string? FullName,
    string? Phone,
    string? SecondaryPhone,
    bool IsStandby,
    string? Comments,
    decimal? HirePrice,
    IReadOnlyList<AdminAddonLineRequest>? Addons);

public sealed record AdminBookingUpdateRequest(
    int Version,
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly? CheckInTime,
    TimeOnly? CheckOutTime,
    string? Email,
    string? FullName,
    string? Phone,
    string? SecondaryPhone,
    bool IsStandby,
    string? Comments,
    decimal HirePrice,
    BookingStatus Status);

public sealed record AdminQuoteDto(
    decimal? HirePrice,
    string? SeasonName,
    DateOnly? EndDate,
    PaymentSchedule? PaymentSchedule,
    string? Message);

public sealed record VersionRequest(int Version);

public sealed record AddAddonLineRequest(int Version, string AddonId, int Quantity);

public sealed record UpdateAddonLineRequest(int Version, int Quantity, decimal? UnitPrice);

public sealed record ManualPaymentRequest(int Version, decimal Amount, DateOnly Date, string? Note);

public sealed record SendPaymentLinkResult(string SentTo, decimal AmountDue);

public sealed record AddonAvailabilityDto(string AddonId, int Capacity, int Remaining);

public sealed record RebookAddonDto(string AddonId, string Name, int Quantity, decimal? UnitPrice);

/// <summary>What "Re-book" pre-fills in the new booking dialog.</summary>
public sealed record RebookDto(
    string OriginalReference,
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    string? Email,
    string? FullName,
    string? Phone,
    string? SecondaryPhone,
    decimal? HirePrice,
    IReadOnlyList<RebookAddonDto> Addons);
