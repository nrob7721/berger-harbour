using BergerHarbour.Application.Customers;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

public sealed record TimelineBoatDto(string Id, string Name, bool IsActive);

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

public sealed record AddonLineDto(
    string Id,
    string AddonId,
    string Name,
    int Quantity,
    decimal? UnitPrice,
    AddonLineStatus Status,
    decimal? LineTotal);

public sealed record PaymentDto(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    PaymentRecordedBy RecordedBy);

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
    int NumberOfGuests,
    string? Comments,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentSchedule PaymentSchedule,
    CustomerDto Customer,
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
    bool? GroupRestrictionDeclaredNotApplicable,
    IReadOnlyList<SentNotification> SentNotifications);

public sealed record AdminBookingRequest(
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfGuests,
    string? Email,
    string? FullName,
    string? Phone,
    bool IsStandby,
    string? Comments,
    decimal? HirePrice);

public sealed record AdminBookingUpdateRequest(
    int Version,
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfGuests,
    string? Email,
    string? FullName,
    string? Phone,
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

public sealed record UpdateAddonLineRequest(int Version, int Quantity, decimal? UnitPrice, AddonLineStatus Status);

public sealed record ManualPaymentRequest(int Version, decimal Amount, DateOnly Date, string? Note);

public sealed record SendPaymentLinkResult(string SentTo, decimal AmountDue);
